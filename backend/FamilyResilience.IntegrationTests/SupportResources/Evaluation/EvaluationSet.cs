using System.Net;
using System.Text.Json;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.SupportResources;
using FamilyResilience.Domain.SupportResources;
using FamilyResilience.IntegrationTests.Support;

namespace FamilyResilience.IntegrationTests.SupportResources.Evaluation;

/// <summary>One synthetic directory entry, shaped exactly as the admin API's draft body.</summary>
internal sealed record EvaluationResource(
    string Code,
    string NameEn,
    string NameAr,
    string SummaryEn,
    string SummaryAr,
    string ProviderNameEn,
    string ProviderNameAr,
    string Url,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Audiences,
    IReadOnlyList<string> ServiceLanguages,
    string Visibility);

/// <summary>
/// One synthetic evaluation query: <see cref="Language"/> is <c>en</c> or <c>ar</c>, <see cref="Scope"/> <c>in</c> or
/// <c>out</c>; <see cref="Relevant"/> lists the codes a participant should be shown (none when out of scope).
/// </summary>
internal sealed record EvaluationQuery(
    string Id,
    string Language,
    string Text,
    IReadOnlyList<string>? Categories,
    IReadOnlyList<string> Relevant,
    string Scope)
{
    public bool InScope => Scope == "in";
}

/// <summary>
/// The synthetic evaluation set (spec §9): <c>synthetic-directory.json</c> and <c>evaluation-queries.json</c>, copied
/// next to the test assembly. <see cref="Load"/> also checks the set's own rules, so a typo in a relevant code fails
/// loudly instead of quietly lowering recall.
/// </summary>
internal sealed class EvaluationSet
{
    private const string AdminRoute = "/api/v1/admin/support-resources";

    private static readonly JsonSerializerOptions FileOptions = new(JsonSerializerDefaults.Web);

    private EvaluationSet(IReadOnlyList<EvaluationResource> resources, IReadOnlyList<EvaluationQuery> queries)
    {
        Resources = resources;
        Queries = queries;
    }

    public IReadOnlyList<EvaluationResource> Resources { get; }

    public IReadOnlyList<EvaluationQuery> Queries { get; }

    public IReadOnlyList<EvaluationQuery> InScope => Queries.Where(query => query.InScope).ToList();

    public IReadOnlyList<EvaluationQuery> OutOfScope => Queries.Where(query => !query.InScope).ToList();

    public static EvaluationSet Load()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "SupportResources", "Evaluation");
        var resources = Read<DirectoryFile>(Path.Combine(directory, "synthetic-directory.json")).Resources;
        var queries = Read<QueryFile>(Path.Combine(directory, "evaluation-queries.json")).Queries;
        Check(resources, queries);
        return new EvaluationSet(resources, queries);
    }

    /// <summary>
    /// Drafts and approves every entry through the admin API, skipping codes already approved (and approving a code
    /// left as a draft), so every test in the collection can call it and only the first one writes. Returns the
    /// approved entry of each code.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, SupportResourceResponse>> EnsureDirectoryAsync(
        ApiFactory factory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);
        using var admin = factory.AsSystemAdministrator();
        var existing = await ListAllAsync(admin, cancellationToken);
        var approved = new Dictionary<string, SupportResourceResponse>(StringComparer.Ordinal);
        foreach (var resource in Resources)
        {
            var versions = existing.Where(item => item.Code == resource.Code).ToList();
            var current = versions.FirstOrDefault(item => item.Status == SupportResourceStatus.Approved);
            if (current is null)
            {
                var draft = versions.FirstOrDefault(item => item.Status == SupportResourceStatus.Draft)
                    ?? await DraftAsync(admin, resource, cancellationToken);
                current = await ApproveAsync(admin, draft.Id, cancellationToken);
            }

            approved[resource.Code] = current;
        }

        return approved;
    }

    private static async Task<List<SupportResourceResponse>> ListAllAsync(HttpClient admin, CancellationToken cancellationToken)
    {
        var items = new List<SupportResourceResponse>();
        for (var page = 1; ; page++)
        {
            using var response = await admin.GetAsync($"{AdminRoute}?pageSize=100&page={page}", cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var listed = await response.ReadAsync<PagedResponse<SupportResourceResponse>>(cancellationToken);
            items.AddRange(listed.Items);
            if (items.Count >= listed.TotalCount || listed.Items.Count == 0)
            {
                return items;
            }
        }
    }

    private static async Task<SupportResourceResponse> DraftAsync(
        HttpClient admin, EvaluationResource resource, CancellationToken cancellationToken)
    {
        using var response = await admin.PostJsonAsync(AdminRoute, resource, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<SupportResourceResponse>(cancellationToken);
    }

    private static async Task<SupportResourceResponse> ApproveAsync(
        HttpClient admin, Guid id, CancellationToken cancellationToken)
    {
        using var response = await admin.PostJsonAsync(
            $"{AdminRoute}/{id}/approve",
            new { approvalSource = "Synthetic evaluation review", reviewDueAt = TestData.Now.AddDays(180) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<SupportResourceResponse>(cancellationToken);
    }

    private static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), FileOptions)
        ?? throw new InvalidOperationException($"{Path.GetFileName(path)} is empty.");

    private static void Check(IReadOnlyList<EvaluationResource> resources, IReadOnlyList<EvaluationQuery> queries)
    {
        Require(resources.Count == 24, "the directory must hold 24 resources");
        Require(
            resources.Select(resource => resource.Code).Distinct(StringComparer.Ordinal).Count() == resources.Count,
            "resource codes must be unique");
        Require(resources.All(IsMarkedSynthetic), "every entry must be marked synthetic");
        var byCategory = resources.GroupBy(resource => resource.Categories[0], StringComparer.Ordinal).ToList();
        Require(byCategory.Count == 6 && byCategory.All(group => group.Count() == 4), "four resources in each of six categories");
        Require(resources.Count(resource => resource.Visibility == "staff_only") == 2, "two staff-only resources");
        Require(
            resources.Count(resource => resource.SummaryEn.Contains("Ignore previous instructions", StringComparison.Ordinal)
                || resource.SummaryEn.Contains("SYSTEM:", StringComparison.Ordinal)) == 2,
            "two entries carry prompt-injection text");

        var visibleToParticipants = resources
            .Where(resource => resource.Visibility == "everyone")
            .Select(resource => resource.Code)
            .ToHashSet(StringComparer.Ordinal);
        Require(
            queries.Select(query => query.Id).Distinct(StringComparer.Ordinal).Count() == queries.Count,
            "query ids must be unique");
        foreach (var query in queries)
        {
            Require(query.Language is "en" or "ar", $"{query.Id}: language must be en or ar");
            Require(
                query.Relevant.All(visibleToParticipants.Contains),
                $"{query.Id}: every relevant code must be a participant-visible resource");
            Require(
                query.Scope switch
                {
                    "in" => query.Relevant.Count is >= 1 and <= 4,
                    "out" => query.Relevant.Count == 0 && query.Categories is null,
                    _ => false,
                },
                $"{query.Id}: in-scope queries list 1 to 4 relevant codes; out-of-scope ones list none and no category");
        }

        Require(queries.Count(query => query is { InScope: true, Language: "en" }) == 20, "20 English in-scope queries");
        Require(queries.Count(query => query is { InScope: true, Language: "ar" }) == 20, "20 Arabic in-scope queries");
        Require(queries.Count(query => query is { InScope: false, Language: "en" }) == 4, "4 English out-of-scope queries");
        Require(queries.Count(query => query is { InScope: false, Language: "ar" }) == 4, "4 Arabic out-of-scope queries");
    }

    private static bool IsMarkedSynthetic(EvaluationResource resource) =>
        resource.NameEn.StartsWith("Synthetic ", StringComparison.Ordinal)
        && resource.NameAr.Contains("تجريبي", StringComparison.Ordinal)
        && resource.ProviderNameEn.StartsWith("Synthetic ", StringComparison.Ordinal)
        && resource.ProviderNameAr.Contains("تجريبي", StringComparison.Ordinal)
        && resource.Url.StartsWith("https://example.org/synthetic/", StringComparison.Ordinal);

    private static void Require(bool condition, string rule)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"The evaluation set breaks a rule: {rule}.");
        }
    }

    private sealed record DirectoryFile(string Description, IReadOnlyList<EvaluationResource> Resources);

    private sealed record QueryFile(string Description, IReadOnlyList<EvaluationQuery> Queries);
}
