using System.Text.Encodings.Web;
using System.Text.Json;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;

namespace FamilyResilience.IntegrationTests.OpenApi;

/// <summary>
/// backend/openapi/v1.json is the committed API contract and must equal the live Development document. To accept a
/// deliberate change, run the tests once with AZWAH_UPDATE_OPENAPI_SNAPSHOT=1 and commit the file.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class OpenApiContractTests(ApiFactory factory)
{
    private const string UpdateVariable = "AZWAH_UPDATE_OPENAPI_SNAPSHOT";
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Fact]
    public async Task The_committed_contract_matches_the_live_document()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var development = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = development.CreateClient();
        var live = Normalize(await client.GetStringAsync("/openapi/v1.json", cancellationToken));
        var folder = Path.Combine(BackendRoot(), "openapi");
        var path = Path.Combine(folder, "v1.json");

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(path, live, cancellationToken);
        }

        Assert.True(File.Exists(path), $"Missing {path}. Run the tests with {UpdateVariable}=1 and commit the file.");
        Assert.Equal(Normalize(await File.ReadAllTextAsync(path, cancellationToken)), live);
    }

    [Theory]
    [InlineData("/api/v1/instruments/{instrumentId}/versions")]
    [InlineData("/api/v1/instrument-versions/{versionId}/approve")]
    [InlineData("/api/v1/instrument-versions/{versionId}/rule-sets")]
    [InlineData("/api/v1/rule-sets/{ruleSetId}/approve")]
    [InlineData("/api/v1/rule-sets/{ruleSetId}/retire")]
    [InlineData("/api/v1/households/{householdId}/evaluate-eligibility")]
    [InlineData("/api/v1/households/{householdId}/assign")]
    [InlineData("/api/v1/assessment-sessions/{sessionId}/score")]
    [InlineData("/api/v1/assessment-sessions/{sessionId}/score-results")]
    public async Task The_contract_documents_the_assessment_engine_endpoints(string route)
    {
        var contract = await File.ReadAllTextAsync(Path.Combine(BackendRoot(), "openapi", "v1.json"), TestContext.Current.CancellationToken);

        Assert.Contains($"\"{route}\"", contract, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_response_in_the_contract_declares_its_single_media_type()
    {
        var contract = await File.ReadAllTextAsync(Path.Combine(BackendRoot(), "openapi", "v1.json"), TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(contract);
        var offenders = new List<string>();

        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (operation.Value.ValueKind != JsonValueKind.Object
                    || !operation.Value.TryGetProperty("responses", out var responses))
                {
                    continue;
                }

                foreach (var response in responses.EnumerateObject())
                {
                    var expected = path.Name == ExportFileRoute && response.Name == "200" ? "text/csv" : ExpectedMediaType(response.Name);
                    if (expected is null || !response.Value.TryGetProperty("content", out var content))
                    {
                        continue;
                    }

                    var mediaTypes = content.EnumerateObject().Select(media => media.Name).ToArray();
                    if (mediaTypes.Length != 1 || mediaTypes[0] != expected)
                    {
                        offenders.Add(
                            $"{operation.Name.ToUpperInvariant()} {path.Name} {response.Name}: {string.Join(", ", mediaTypes)}");
                    }
                }
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>The only response that is not JSON: an export's CSV file.</summary>
    private const string ExportFileRoute = "/api/v1/admin/exports/{exportId}/file";

    private static string? ExpectedMediaType(string status) => status switch
    {
        ['2', ..] => "application/json",
        ['4' or '5', ..] => "application/problem+json",
        _ => null,
    };

    private static string Normalize(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement, Indented) + "\n";
    }

    private static string BackendRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FamilyResilience.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("FamilyResilience.slnx was not found above the test output folder.");
    }
}
