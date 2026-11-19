using System.Net;
using System.Text.Json;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.IntegrationTests.Api;

[Collection(ApiCollection.Name)]
public sealed class InstrumentVersionEndpointsTests(ApiFactory factory)
{
    private static object Option(string code, int value, string? arabic) =>
        new { code, displayOrder = value, value, isNonSubstantive = false, label = new { ar = arabic, en = $"Option {code}" } };

    private static object Document(
        string itemType = "single_choice", string direction = "unspecified", bool arabicLabels = true, string secondItemCode = "Q2") =>
        new
        {
            contentReferenceAr = "test (ar)",
            contentReferenceEn = "test (en)",
            sections = new[]
            {
                new
                {
                    code = "A",
                    displayOrder = 1,
                    title = new { ar = "قسم", en = "Section" },
                    items = new[] { "Q1", secondItemCode }.Select((code, index) => new
                    {
                        code,
                        displayOrder = index + 1,
                        itemType,
                        isRequired = true,
                        scoringCategory = (string?)null,
                        scoringDirection = direction,
                        prompt = new { ar = "سؤال", en = "Question" },
                        options = new[] { Option("1", 1, arabicLabels ? "خيار" : null), Option("2", 2, "خيار") },
                    }).ToArray(),
                },
            },
        };

    private static async Task<InstrumentResponse> InstrumentAsync(
        HttpClient client, CancellationToken cancellationToken, string? arabicName = "أداة")
    {
        using var response = await client.PostJsonAsync(
            "/api/v1/instruments",
            new { code = TestData.UniqueCode("api_versions"), kind = "family_voices", name = new { ar = arabicName, en = "Survey" }, description = new { ar = "وصف", en = "About" } },
            cancellationToken);
        return await response.ReadAsync<InstrumentResponse>(cancellationToken);
    }

    private static async Task<InstrumentVersionResponse> ImportAsync(
        HttpClient client, Guid instrumentId, CancellationToken cancellationToken, bool arabicLabels = true)
    {
        using var response = await client.PostJsonAsync(
            $"/api/v1/instruments/{instrumentId}/versions", Document(arabicLabels: arabicLabels), cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<InstrumentVersionResponse>(cancellationToken);
    }

    private static object Approval(DateTimeOffset effectiveFrom) =>
        new { approvalSource = "Synthetic test approval", effectiveFrom, effectiveTo = (DateTimeOffset?)null };

    [Fact]
    public async Task Importing_twice_creates_draft_versions_1_and_2()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();
        var instrument = await InstrumentAsync(client, cancellationToken);

        var first = await ImportAsync(client, instrument.Id, cancellationToken);
        var second = await ImportAsync(client, instrument.Id, cancellationToken);

        Assert.Equal((1, InstrumentVersionStatus.Draft), (first.VersionNumber, first.Status));
        Assert.Equal(2, second.VersionNumber);
        Assert.Equal(new[] { "Q1", "Q2" }, first.Sections[0].Items.Select(item => item.Code));
    }

    [Fact]
    public async Task Importing_into_an_unknown_instrument_returns_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync($"/api/v1/instruments/{Guid.NewGuid()}/versions", Document(), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("free_text", "unspecified")]
    [InlineData("single_choice", "backwards")]
    public async Task An_import_with_an_unknown_enum_value_returns_400_not_500(string itemType, string direction)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();
        var instrument = await InstrumentAsync(client, cancellationToken);

        using var response = await client.PostJsonAsync(
            $"/api/v1/instruments/{instrument.Id}/versions", Document(itemType, direction), cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_import_with_a_duplicate_item_code_returns_400_naming_where()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();
        var instrument = await InstrumentAsync(client, cancellationToken);

        using var response = await client.PostJsonAsync(
            $"/api/v1/instruments/{instrument.Id}/versions", Document(secondItemCode: "Q1"), cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("questionnaires.item_code_taken", await response.ProblemCodeAsync(cancellationToken));
        Assert.Contains("(item Q1)", await response.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
    }

    // Each case edits the first option of item Q1. A lenient reader would accept all five, and the second would quietly
    // turn a non-substantive option into a substantive answer.
    [Theory]
    [InlineData("\"isNonSubstantive\":false", "\"isNonSubstantive\":false,\"colour\":\"red\"")]
    [InlineData("\"isNonSubstantive\":false,", "")]
    [InlineData("\"value\":1,", "\"value\":\"1\",")]
    [InlineData("\"isNonSubstantive\":false", "\"isNonSubstantive\":false,\"isNonSubstantive\":true")]
    [InlineData("\"isNonSubstantive\"", "\"IsNonSubstantive\"")]
    public async Task An_import_that_is_not_strict_content_json_returns_400_at_the_path_and_saves_nothing(string original, string replacement)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();
        var instrument = await InstrumentAsync(client, cancellationToken);
        var json = JsonSerializer.Serialize(Document(), ApiJson.Options).ReplaceFirst(original, replacement);

        using var response = await client.PostRawJsonAsync($"/api/v1/instruments/{instrument.Id}/versions", json, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            await response.ErrorKeysAsync(cancellationToken),
            key => key.StartsWith("$.sections[0].items[0].options[0]", StringComparison.Ordinal));
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("FamilyResilience", body, StringComparison.Ordinal);
        Assert.DoesNotContain("deserialization", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Path:", body, StringComparison.Ordinal);
        var instrumentId = InstrumentId.From(instrument.Id);
        Assert.False(await factory.QueryAsync(db => db.InstrumentVersions.AnyAsync(version => version.InstrumentId == instrumentId, cancellationToken)));
    }

    [Fact]
    public async Task A_draft_shows_missing_arabic_as_null()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();
        var instrument = await InstrumentAsync(client, cancellationToken);
        var imported = await ImportAsync(client, instrument.Id, cancellationToken, arabicLabels: false);

        using var response = await client.GetAsync($"/api/v1/instrument-versions/{imported.Id}", cancellationToken);

        var version = await response.ReadAsync<InstrumentVersionResponse>(cancellationToken);
        Assert.Null(version.Sections[0].Items[0].Options[0].Label.Ar);
        Assert.False(version.HasCompleteText);
    }

    [Fact]
    public async Task A_bilingual_draft_has_complete_text_only_once_its_instrument_has_arabic_too()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();
        var instrument = await InstrumentAsync(client, cancellationToken, arabicName: null);
        var imported = await ImportAsync(client, instrument.Id, cancellationToken);
        using var before = await client.GetAsync($"/api/v1/instrument-versions/{imported.Id}", cancellationToken);

        using var completed = await client.PutJsonAsync(
            $"/api/v1/instruments/{instrument.Id}/text",
            new { name = new { ar = "أداة", en = "Survey" }, description = new { ar = "وصف", en = "About" } },
            cancellationToken);
        using var after = await client.GetAsync($"/api/v1/instrument-versions/{imported.Id}", cancellationToken);

        Assert.False(imported.HasCompleteText);
        Assert.False((await before.ReadAsync<InstrumentVersionResponse>(cancellationToken)).HasCompleteText);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.True((await after.ReadAsync<InstrumentVersionResponse>(cancellationToken)).HasCompleteText);
    }

    [Fact]
    public async Task An_unknown_version_returns_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();

        using var response = await client.GetAsync($"/api/v1/instrument-versions/{Guid.NewGuid()}", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("questionnaires.version_not_found", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task Approving_a_complete_draft_publishes_it_with_the_caller_as_approver()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();
        var instrument = await InstrumentAsync(client, cancellationToken);
        var draft = await ImportAsync(client, instrument.Id, cancellationToken);

        using var response = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{draft.Id}/approve", Approval(TestData.Now.AddDays(-10)), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var approved = await response.ReadAsync<InstrumentVersionResponse>(cancellationToken);
        Assert.Equal(InstrumentVersionStatus.Approved, approved.Status);
        Assert.Equal(ApiClients.SystemAdministratorSubject, approved.ApprovedBy);
    }

    [Fact]
    public async Task Approving_a_draft_with_missing_arabic_returns_409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();
        var instrument = await InstrumentAsync(client, cancellationToken);
        var draft = await ImportAsync(client, instrument.Id, cancellationToken, arabicLabels: false);

        using var response = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{draft.Id}/approve", Approval(TestData.Now.AddDays(-10)), cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("questionnaires.arabic_text_missing", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task An_approval_with_a_non_utc_time_returns_400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();
        var instrument = await InstrumentAsync(client, cancellationToken);
        var draft = await ImportAsync(client, instrument.Id, cancellationToken);

        using var response = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{draft.Id}/approve",
            new { approvalSource = "Synthetic test approval", effectiveFrom = "2026-11-01T00:00:00+04:00" },
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"effectiveFrom\"", await response.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approving_an_unknown_version_returns_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{Guid.NewGuid()}/approve", Approval(TestData.Now), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_new_draft_copies_an_approved_version()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();
        var instrument = await InstrumentAsync(client, cancellationToken);
        var draft = await ImportAsync(client, instrument.Id, cancellationToken);
        using var approved = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{draft.Id}/approve", Approval(TestData.Now.AddDays(-10)), cancellationToken);

        using var response = await client.PostJsonAsync($"/api/v1/instrument-versions/{draft.Id}/drafts", new { }, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var copy = await response.ReadAsync<InstrumentVersionResponse>(cancellationToken);
        Assert.Equal((2, InstrumentVersionStatus.Draft), (copy.VersionNumber, copy.Status));
        Assert.Equal(new[] { "Q1", "Q2" }, copy.Sections[0].Items.Select(item => item.Code));
        Assert.Equal($"/api/v1/instrument-versions/{copy.Id}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task An_approved_version_can_have_its_end_scheduled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();
        var instrument = await InstrumentAsync(client, cancellationToken);
        var draft = await ImportAsync(client, instrument.Id, cancellationToken);
        using var approved = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{draft.Id}/approve", Approval(TestData.Now.AddDays(-10)), cancellationToken);

        using var response = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{draft.Id}/schedule-end", new { effectiveTo = TestData.Now.AddDays(5) }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TestData.Now.AddDays(5), (await response.ReadAsync<InstrumentVersionResponse>(cancellationToken)).EffectiveTo);
    }

    [Fact]
    public async Task An_approved_version_is_retired_from_now_but_not_from_the_future()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();
        var instrument = await InstrumentAsync(client, cancellationToken);
        var draft = await ImportAsync(client, instrument.Id, cancellationToken);
        using var approved = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{draft.Id}/approve", Approval(TestData.Now.AddDays(-10)), cancellationToken);

        using var future = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{draft.Id}/retire", new { effectiveTo = TestData.Now.AddDays(1) }, cancellationToken);
        using var now = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{draft.Id}/retire", new { effectiveTo = TestData.Now }, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, future.StatusCode);
        Assert.Equal("questionnaires.retire_in_future", await future.ProblemCodeAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.OK, now.StatusCode);
        Assert.Equal(InstrumentVersionStatus.Retired, (await now.ReadAsync<InstrumentVersionResponse>(cancellationToken)).Status);
    }

    private static async Task<InstrumentVersionResponse> ApprovedAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var instrument = await InstrumentAsync(client, cancellationToken);
        var draft = await ImportAsync(client, instrument.Id, cancellationToken);
        using var approved = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{draft.Id}/approve", Approval(TestData.Now.AddDays(-10)), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        return draft;
    }

    [Fact]
    public async Task An_approval_without_an_effective_from_returns_400_and_leaves_the_draft()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();
        var instrument = await InstrumentAsync(client, cancellationToken);
        var draft = await ImportAsync(client, instrument.Id, cancellationToken);

        using var response = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{draft.Id}/approve", new { approvalSource = "Synthetic test approval" }, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"effectiveFrom\"", await response.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
        using var fetched = await client.GetAsync($"/api/v1/instrument-versions/{draft.Id}", cancellationToken);
        Assert.Equal(InstrumentVersionStatus.Draft, (await fetched.ReadAsync<InstrumentVersionResponse>(cancellationToken)).Status);
    }

    [Fact]
    public async Task Scheduling_an_end_without_a_time_returns_400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{Guid.NewGuid()}/schedule-end", new { }, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"effectiveTo\"", await response.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approving_writes_an_audit_event_naming_the_caller()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();
        var draft = await ApprovedAsync(client, cancellationToken);

        var events = await factory.QueryAsync(db => db.AuditEvents
            .Where(auditEvent => auditEvent.ResourceId == draft.Id.ToString())
            .ToListAsync(cancellationToken));

        var approved = Assert.Single(events, auditEvent => auditEvent.Action == "questionnaires.instrument_version.approved");
        Assert.Equal("instrument_version", approved.ResourceType);
        Assert.Equal(ApiClients.SystemAdministratorSubject, approved.ActorId);
    }

    [Fact]
    public async Task Copying_an_unknown_version_returns_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync($"/api/v1/instrument-versions/{Guid.NewGuid()}/drafts", new { }, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("questionnaires.version_not_found", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task Scheduling_an_end_with_a_non_utc_time_returns_400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{Guid.NewGuid()}/schedule-end", new { effectiveTo = "2026-11-01T00:00:00+04:00" }, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"effectiveTo\"", await response.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scheduling_the_end_of_an_unknown_version_returns_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{Guid.NewGuid()}/schedule-end", new { effectiveTo = TestData.Now.AddDays(5) }, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("questionnaires.version_not_found", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task Scheduling_a_different_end_twice_returns_409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();
        var draft = await ApprovedAsync(client, cancellationToken);
        using var first = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{draft.Id}/schedule-end", new { effectiveTo = TestData.Now.AddDays(5) }, cancellationToken);

        using var second = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{draft.Id}/schedule-end", new { effectiveTo = TestData.Now.AddDays(6) }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("questionnaires.effective_to_already_set", await second.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task Retiring_with_a_non_utc_time_returns_400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{Guid.NewGuid()}/retire", new { effectiveTo = "2026-10-01T00:00:00+04:00" }, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"effectiveTo\"", await response.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Retiring_an_unknown_version_returns_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{Guid.NewGuid()}/retire", new { effectiveTo = TestData.Now }, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("questionnaires.version_not_found", await response.ProblemCodeAsync(cancellationToken));
    }
}
