using System.Net;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.SupportResources;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FamilyResilience.IntegrationTests.SupportResources;

/// <summary>
/// The dormant language-model path, switched on in test hosts only with a scripted model registered by the test. No
/// provider is approved or configured. The shared database holds other tests' resources, so each test uses its own
/// category and its own made-up words. Every entry is synthetic.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class LanguageModelModeTests(ApiFactory factory)
{
    private const string AdminRoute = "/api/v1/admin/support-resources";
    private const string SearchRoute = "/api/v1/support-resources/search";

    /// <summary>A letters-only word no other test uses, so it survives query parsing as one term.</summary>
    private static string UniqueWord() =>
        string.Concat(Guid.NewGuid().ToString("N").Select(c => (char)(c is >= '0' and <= '9' ? 'k' + (c - '0') : c)));

    private static string UniqueCategory() => TestData.UniqueCode("synthetic_cat");

    private static string Describe(Exception exception) =>
        exception.InnerException is null ? exception.Message : $"{exception.Message} | {Describe(exception.InnerException)}";

    /// <summary>
    /// A host with the model switched on (approval reference <c>TEST-ONLY</c>), <paramref name="model"/> registered when
    /// given, and any other settings overridden.
    /// </summary>
    private WebApplicationFactory<Program> ModelHost(
        ScriptedLanguageModel? model, Dictionary<string, string?>? settings = null, CapturingLoggerProvider? logs = null)
    {
        var configuration = new Dictionary<string, string?>
        {
            ["SupportResources:LanguageModel:Enabled"] = "true",
            ["SupportResources:LanguageModel:ApprovalReference"] = "TEST-ONLY",
        };
        foreach (var (key, value) in settings ?? [])
        {
            configuration[key] = value;
        }

        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, sources) => sources.AddInMemoryCollection(configuration));
            builder.ConfigureTestServices(services =>
            {
                if (model is not null)
                {
                    services.AddSingleton<ILanguageModelClient>(model);
                }
            });
            if (logs is not null)
            {
                // Every level, for this provider only, so the request's own log entries are certainly captured.
                builder.ConfigureLogging(logging => logging.AddProvider(logs).AddFilter<CapturingLoggerProvider>(null, LogLevel.Trace));
            }
        });
    }

    private static HttpClient Client(WebApplicationFactory<Program> host, string role)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ApiClients.NewSubject("reader"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, role);
        return client;
    }

    private async Task<SupportResourceResponse> ApprovedAsync(
        string category, string nameEn, CancellationToken cancellationToken, string visibility = "everyone")
    {
        using var admin = factory.AsSystemAdministrator();
        using var drafted = await admin.PostJsonAsync(
            AdminRoute,
            new
            {
                code = TestData.UniqueCode("resource"),
                nameAr = "مورد تجريبي",
                nameEn,
                summaryAr = "ملخص تجريبي",
                summaryEn = "Synthetic summary",
                providerNameAr = "جهة تجريبية",
                providerNameEn = "Synthetic provider",
                url = $"https://example.org/{Guid.NewGuid():N}",
                categories = new[] { category },
                audiences = new[] { "adults" },
                serviceLanguages = new[] { "ar", "en" },
                visibility,
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, drafted.StatusCode);
        var draft = await drafted.ReadAsync<SupportResourceResponse>(cancellationToken);

        using var approved = await admin.PostJsonAsync(
            $"{AdminRoute}/{draft.Id}/approve",
            new { approvalSource = "Synthetic review", reviewDueAt = TestData.Now.AddDays(180) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        return await approved.ReadAsync<SupportResourceResponse>(cancellationToken);
    }

    private static async Task<SupportResourceDiscoveryResponse> SearchOkAsync(
        HttpClient client, object body, CancellationToken cancellationToken)
    {
        using var response = await client.PostJsonAsync(SearchRoute, body, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<SupportResourceDiscoveryResponse>(cancellationToken);
    }

    [Fact]
    public void The_api_refuses_to_start_with_the_model_enabled_and_no_provider()
    {
        using var host = ModelHost(model: null);

        var error = Assert.ThrowsAny<Exception>(() => host.CreateClient());

        var message = Describe(error);
        Assert.Contains("SupportResources:LanguageModel is enabled but cannot run: provider_missing", message, StringComparison.Ordinal);
        Assert.DoesNotContain("TEST-ONLY", message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_api_refuses_to_start_with_the_model_enabled_and_no_approval_reference()
    {
        using var host = ModelHost(
            ScriptedLanguageModel.Reversing(), new() { ["SupportResources:LanguageModel:ApprovalReference"] = "" });

        var error = Assert.ThrowsAny<Exception>(() => host.CreateClient());

        var message = Describe(error);
        Assert.Contains("approval_reference_missing", message, StringComparison.Ordinal);
        Assert.DoesNotContain("provider_missing", message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_api_refuses_to_start_with_the_model_enabled_and_a_setting_out_of_range()
    {
        using var host = ModelHost(
            ScriptedLanguageModel.Reversing(), new() { ["SupportResources:LanguageModel:TimeoutSeconds"] = "120" });

        var error = Assert.ThrowsAny<Exception>(() => host.CreateClient());

        Assert.Contains("timeout_out_of_range", Describe(error), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_api_starts_with_the_model_off_and_no_provider()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        Assert.False(services.GetRequiredService<IServiceProviderIsService>().IsService(typeof(ILanguageModelClient)));
        Assert.Equal(LanguageModelSettings.Default, services.GetRequiredService<LanguageModelSettings>());
        Assert.IsType<DeterministicSupportResourceAssistant>(services.GetRequiredService<ISupportResourceAssistant>());
    }

    [Fact]
    public async Task A_scripted_model_orders_the_results()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (category, word) = (UniqueCategory(), UniqueWord());
        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        for (var i = 0; i < 3; i++)
        {
            var name = $"Synthetic {word} {UniqueWord()}";
            ids[name] = (await ApprovedAsync(category, name, cancellationToken)).Id;
        }

        var model = ScriptedLanguageModel.Reversing();
        await using var host = ModelHost(model);
        using var participant = Client(host, Roles.Participant);

        var found = await SearchOkAsync(participant, new { text = word, categories = new[] { category } }, cancellationToken);

        Assert.Equal(
            (DiscoveryOutcome.Matched, DiscoveryMode.LanguageModel, (DiscoveryFallback?)null, DiscoveryNotice.InformationOnly),
            (found.Outcome, found.Mode, found.Fallback, found.Notice));
        var offered = ScriptedLanguageModel.Candidates(Assert.Single(model.Requests));
        Assert.Equal(3, offered.Count);
        Assert.Equal(offered.Select(candidate => ids[candidate.NameEn]).Reverse(), found.Matches.Select(match => match.Id));
        Assert.Equal([1, 2, 3], found.Matches.Select(match => match.Rank));
    }

    [Fact]
    public async Task A_failing_model_falls_back_and_search_still_answers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (category, word) = (UniqueCategory(), UniqueWord());
        var resource = await ApprovedAsync(category, $"Synthetic {word}", cancellationToken);
        var model = ScriptedLanguageModel.Failing();
        await using var host = ModelHost(model);
        using var participant = Client(host, Roles.Participant);

        var found = await SearchOkAsync(participant, new { text = word, categories = new[] { category } }, cancellationToken);

        Assert.Equal(
            (DiscoveryOutcome.Matched, DiscoveryMode.Deterministic, (DiscoveryFallback?)DiscoveryFallback.ModelUnavailable),
            (found.Outcome, found.Mode, found.Fallback));
        Assert.Equal(resource.Id, Assert.Single(found.Matches).Id);
        Assert.Single(model.Requests);
    }

    [Fact]
    public async Task A_model_naming_an_unknown_resource_is_ignored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (category, word) = (UniqueCategory(), UniqueWord());
        var created = new List<Guid>
        {
            (await ApprovedAsync(category, $"Synthetic {word} {UniqueWord()}", cancellationToken)).Id,
            (await ApprovedAsync(category, $"Synthetic {word} {UniqueWord()}", cancellationToken)).Id,
        };
        var model = ScriptedLanguageModel.Answering("""{"selected":["r1","r42"]}""");
        await using var host = ModelHost(model);
        using var participant = Client(host, Roles.Participant);

        var found = await SearchOkAsync(participant, new { text = word, categories = new[] { category } }, cancellationToken);

        Assert.Equal(
            (DiscoveryMode.Deterministic, (DiscoveryFallback?)DiscoveryFallback.ModelOutputUngrounded),
            (found.Mode, found.Fallback));
        Assert.Equal(created.Order(), found.Matches.Select(match => match.Id).Order());
    }

    [Fact]
    public async Task Staff_only_resources_never_reach_a_participants_model()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (category, word) = (UniqueCategory(), UniqueWord());
        var (everyoneTag, staffTag) = (UniqueWord(), UniqueWord());
        await ApprovedAsync(category, $"Synthetic {word} {everyoneTag}", cancellationToken);
        var staffOnly = await ApprovedAsync(category, $"Synthetic {word} {staffTag}", cancellationToken, "staff_only");
        var model = ScriptedLanguageModel.Reversing();
        await using var host = ModelHost(model);
        using var participant = Client(host, Roles.Participant);
        using var coordinator = Client(host, Roles.SupportCoordinator);
        var body = new { text = word, categories = new[] { category } };

        var forParticipant = await SearchOkAsync(participant, body, cancellationToken);
        var forCoordinator = await SearchOkAsync(coordinator, body, cancellationToken);

        Assert.Equal(2, model.Requests.Count);
        Assert.Contains(everyoneTag, model.Requests[0].Data, StringComparison.Ordinal);
        Assert.DoesNotContain(staffTag, model.Requests[0].Data, StringComparison.Ordinal);
        Assert.DoesNotContain(forParticipant.Matches, match => match.Id == staffOnly.Id);
        // The staff-only entry is a real candidate for staff, so its absence above is the visibility rule at work.
        Assert.Contains(staffTag, model.Requests[1].Data, StringComparison.Ordinal);
        Assert.Contains(forCoordinator.Matches, match => match.Id == staffOnly.Id);
    }

    [Fact]
    public async Task Model_mode_never_logs_search_text()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var logs = new CapturingLoggerProvider();
        var model = ScriptedLanguageModel.Reversing();
        await using var host = ModelHost(model, logs: logs);
        using var measurements = new SupportResourceMeasurements(host);
        using var participant = Client(host, Roles.Participant);
        var (category, word) = (UniqueCategory(), UniqueWord());
        await ApprovedAsync(category, $"Synthetic {word}", cancellationToken);
        var body = new { text = $"zqxmarker {word} 0501234567", categories = new[] { category } };
        var startup = logs.Entries.Count;

        var chosen = await SearchOkAsync(participant, body, cancellationToken);
        model.Respond = ScriptedLanguageModel.Fail;
        var failed = await SearchOkAsync(participant, body, cancellationToken);
        model.Respond = _ => new LanguageModelReply("Synthetic prose instead of JSON", 5, 5);
        var invalid = await SearchOkAsync(participant, body, cancellationToken);

        Assert.Equal((DiscoveryMode.LanguageModel, (DiscoveryFallback?)null), (chosen.Mode, chosen.Fallback));
        Assert.Equal((DiscoveryMode.Deterministic, (DiscoveryFallback?)DiscoveryFallback.ModelUnavailable), (failed.Mode, failed.Fallback));
        Assert.Equal((DiscoveryMode.Deterministic, (DiscoveryFallback?)DiscoveryFallback.ModelOutputInvalid), (invalid.Mode, invalid.Fallback));
        // The model did receive the words (only the digits are redacted), so they were there to leak.
        Assert.Contains("zqxmarker", model.Requests[0].Data, StringComparison.Ordinal);
        var requests = logs.Entries.Skip(startup).ToList();
        Assert.NotEmpty(requests);
        Assert.Contains(requests, entry => entry.Category.StartsWith("Microsoft.AspNetCore.Hosting", StringComparison.Ordinal));
        Assert.DoesNotContain(requests, entry => entry.Message.Contains("zqxmarker", StringComparison.OrdinalIgnoreCase)
            || entry.Message.Contains("0501234567", StringComparison.Ordinal)
            || entry.Message.Contains(word, StringComparison.OrdinalIgnoreCase));
        var recorded = measurements.All;
        Assert.NotEmpty(recorded);
        Assert.All(recorded.SelectMany(measurement => measurement.Tags.Values), value =>
        {
            Assert.DoesNotContain("zqxmarker", value, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("0501234567", value, StringComparison.Ordinal);
            Assert.DoesNotContain(word, value, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(category, value, StringComparison.Ordinal);
        });
        Assert.Equal(1, measurements.Total(
            "azwah.support_resources.searches", ("mode", "language_model"), ("outcome", "matched"), ("fallback", "none")));
        Assert.Equal(1, measurements.Total("azwah.support_resources.searches", ("fallback", "model_unavailable")));
        Assert.Equal(1, measurements.Total("azwah.support_resources.searches", ("fallback", "model_output_invalid")));
        Assert.Equal(1, measurements.Total("azwah.support_resources.model_calls", ("result", "ok")));
        Assert.Equal(1, measurements.Total("azwah.support_resources.model_calls", ("result", "unavailable")));
        Assert.Equal(1, measurements.Total("azwah.support_resources.model_calls", ("result", "invalid")));
    }
}
