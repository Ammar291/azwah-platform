using System.Net;
using System.Text.Json;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Application.SupportResources;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.IntegrationTests.Assistant;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.SupportResources;

/// <summary>
/// The directory search screens a participant's text for crisis phrases (D-135, D-138): a match raises an alert with
/// source <c>support_search</c> and the answer carries the crisis notice. Staff searches are never screened.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SupportSearchSafetyTests(ApiFactory factory)
{
    private const string Route = "/api/v1/support-resources/search";
    private const string CrisisText = "I want to end my life";

    private static async Task<JsonDocument> SearchAsync(HttpClient client, string text, CancellationToken cancellationToken)
    {
        using var response = await client.PostJsonAsync(Route, new { text }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task Participant_search_with_crisis_text_raises_alert_and_notice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = await factory.RegisteredParticipantAsync(cancellationToken);
        using var participant = factory.AsParticipant(subject);

        using var answer = await SearchAsync(participant, CrisisText, cancellationToken);

        Assert.Equal(SafetyNotices.CrisisSupport, answer.RootElement.GetProperty("safetyNotice").GetString());
        Assert.Equal("information_only", answer.RootElement.GetProperty("notice").GetString());
        var alert = Assert.Single(await factory.AlertsOfAsync(subject, cancellationToken));
        Assert.Equal(
            (SafetyConcern.SuicideOrSelfHarm, SafetySeverity.Critical, SafetyAlertSource.SupportSearch, SafetyDetector.PhraseScreen),
            (alert.Concern, alert.Severity, alert.Source, alert.Detector));
        Assert.Equal(("suicide.intent", (string?)null, 1), (alert.SignalCode, alert.Language, alert.OccurrenceCount));
        var raised = Assert.Single(await factory.AlertAuditsAsync(alert.Id, cancellationToken));
        Assert.Equal((RecordSafetySignal.Raised, AuditActorType.System, (string?)null), (raised.Action, raised.ActorType, raised.ActorId));
        var stored = await factory.StoredRowsOfAsync(subject, cancellationToken);
        Assert.Contains(stored, row => row.Contains(RecordSafetySignal.Raised, StringComparison.Ordinal));
        Assert.DoesNotContain(stored, row => row.Contains("end my life", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A directory whose database is down.</summary>
    private sealed class FailingDirectory : ISupportResourceAssistant
    {
        public Task<SupportResourceDiscovery> DiscoverAsync(SupportResourceQuery query, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Synthetic directory fault.");
    }

    [Fact]
    public async Task A_search_that_fails_after_crisis_text_still_raises_the_alert_and_its_problem_carries_the_notice()
    {
        // The notice must never depend on the database (D-138): the fault is a generic 500 that still carries it.
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = await factory.RegisteredParticipantAsync(cancellationToken);
        await using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddScoped<ISupportResourceAssistant, FailingDirectory>()));
        using var participant = host.Client(subject);

        using var response = await participant.PostJsonAsync(Route, new { text = CrisisText }, cancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(SafetyNotices.CrisisSupport, problem.RootElement.GetProperty("safetyNotice").GetString());
        Assert.False(problem.RootElement.TryGetProperty("code", out _));
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
        Assert.Equal(SafetyAlertSource.SupportSearch, Assert.Single(await factory.AlertsOfAsync(subject, cancellationToken)).Source);
    }

    [Theory]
    [InlineData(Roles.SupportCoordinator)]
    [InlineData(Roles.SystemAdministrator)]
    public async Task Staff_search_with_crisis_text_raises_nothing(string role)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = ApiClients.NewSubject("staff");
        // A platform user, so a screened search would have someone to raise an alert for.
        await factory.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IPlatformUserRepository>().Add(TestData.Ok(PlatformUser.Register(subject, TestData.Now)));
                return Task.CompletedTask;
            },
            cancellationToken);
        using var staff = factory.As(subject, role);

        using var answer = await SearchAsync(staff, CrisisText, cancellationToken);

        Assert.Equal(JsonValueKind.Null, answer.RootElement.GetProperty("safetyNotice").ValueKind);
        Assert.Empty(await factory.AlertsOfAsync(subject, cancellationToken));
        Assert.DoesNotContain(await factory.StoredRowsOfAsync(subject, cancellationToken), row => row.Contains("safeguarding.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ordinary_search_has_no_notice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = await factory.RegisteredParticipantAsync(cancellationToken);
        using var participant = factory.AsParticipant(subject);

        using var answer = await SearchAsync(participant, "help with household debts", cancellationToken);

        Assert.Equal(JsonValueKind.Null, answer.RootElement.GetProperty("safetyNotice").ValueKind);
        Assert.Equal(
            ["outcome", "mode", "fallback", "notice", "matches", "safetyNotice"],
            answer.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Empty(await factory.AlertsOfAsync(subject, cancellationToken));
    }
}
