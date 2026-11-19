using System.Net;
using System.Text.Json;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Api;

[Collection(ApiCollection.Name)]
public sealed class RuleSetEndpointsTests(ApiFactory factory)
{
    private static object Eligibility(object rule, string code, string section = "A") =>
        new { kind = "eligibility", code, versionNumber = 1, definition = new { schema = "eligibility.v1", target = new { sectionCode = section }, rule } };

    private static async Task<RuleSetResponse> ProposeAsync(HttpClient client, Guid versionId, object body, CancellationToken cancellationToken)
    {
        using var response = await client.PostJsonAsync($"/api/v1/instrument-versions/{versionId}/rule-sets", body, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<RuleSetResponse>(cancellationToken);
    }

    private static async Task AssertValidationKeyAsync(HttpResponseMessage response, string key, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty(key, out _), $"errors has no key '{key}'.");
    }

    [Fact]
    public async Task Proposing_a_valid_eligibility_rule_returns_201_proposed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await factory.ApprovedVersionAsync(cancellationToken);
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{version.Id.Value}/rule-sets",
            Eligibility(new { ageAtLeast = 60 }, TestData.UniqueCode("rules")), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var proposed = await response.ReadAsync<RuleSetResponse>(cancellationToken);
        Assert.Equal(RuleSetStatus.Proposed, proposed.Status);
        Assert.Equal($"/api/v1/rule-sets/{proposed.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(60, proposed.Definition.GetProperty("rule").GetProperty("ageAtLeast").GetInt32());
    }

    [Fact]
    public async Task An_invalid_definition_returns_400_naming_the_path()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await factory.ApprovedVersionAsync(cancellationToken);
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{version.Id.Value}/rule-sets",
            Eligibility(new { livesInVilla = true }, TestData.UniqueCode("rules")), cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("eligibility.definition_invalid", await response.ProblemCodeAsync(cancellationToken));
        Assert.Contains("$.rule.livesInVilla", await response.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_definition_that_is_not_an_object_returns_400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await factory.ApprovedVersionAsync(cancellationToken);
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{version.Id.Value}/rule-sets",
            new { kind = "eligibility", code = TestData.UniqueCode("rules"), versionNumber = 1, definition = new[] { 1 } }, cancellationToken);

        await AssertValidationKeyAsync(response, "definition", cancellationToken);
    }

    [Fact]
    public async Task A_body_without_a_definition_returns_400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await factory.ApprovedVersionAsync(cancellationToken);
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{version.Id.Value}/rule-sets",
            new { kind = "eligibility", code = TestData.UniqueCode("rules"), versionNumber = 1 }, cancellationToken);

        await AssertValidationKeyAsync(response, "definition", cancellationToken);
    }

    [Fact]
    public async Task A_referral_rule_set_needs_a_valid_referral_definition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await factory.ApprovedVersionAsync(cancellationToken);
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{version.Id.Value}/rule-sets",
            new { kind = "referral", code = TestData.UniqueCode("rules"), versionNumber = 1, definition = new { } }, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("referrals.definition_invalid", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task A_referral_rule_set_for_a_section_of_the_version_is_proposed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await factory.ApprovedVersionAsync(cancellationToken);
        using var client = factory.AsSystemAdministrator();
        var definition = new
        {
            schema = "referral.v1",
            target = new { sectionCode = "A" },
            routes = new[] { new { categoryCode = "synthetic_support", when = new { tierCodeIn = new[] { "band_low" } } } },
        };

        using var proposed = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{version.Id.Value}/rule-sets",
            new { kind = "referral", code = TestData.UniqueCode("rules"), versionNumber = 1, definition }, cancellationToken);
        using var unknownSection = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{version.Id.Value}/rule-sets",
            new { kind = "referral", code = TestData.UniqueCode("rules"), versionNumber = 1, definition = definition with { target = new { sectionCode = "Z" } } },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, proposed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownSection.StatusCode);
        Assert.Equal("questionnaires.section_not_found", await unknownSection.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task A_scoring_rule_naming_an_unknown_policy_returns_409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await factory.ApprovedVersionAsync(cancellationToken);
        using var client = factory.AsSystemAdministrator();
        var body = new
        {
            kind = "scoring",
            code = TestData.UniqueCode("rules"),
            versionNumber = 1,
            definition = new
            {
                schema = "scoring.v1",
                target = new { sectionCode = "A" },
                policy = "weighted_sum",
                algorithmVersion = 1,
                parameters = new { reverseScoring = "mirror_scale", unanswered = "reject", total = true },
            },
        };

        using var response = await client.PostJsonAsync($"/api/v1/instrument-versions/{version.Id.Value}/rule-sets", body, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("scoring.policy_unsupported", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task Proposing_for_an_unknown_version_returns_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{Guid.NewGuid()}/rule-sets",
            Eligibility(new { ageAtLeast = 60 }, TestData.UniqueCode("rules")), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_same_code_and_version_cannot_be_proposed_twice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await factory.ApprovedVersionAsync(cancellationToken);
        using var client = factory.AsSystemAdministrator();
        var body = Eligibility(new { ageAtLeast = 60 }, TestData.UniqueCode("rules"));
        await ProposeAsync(client, version.Id.Value, body, cancellationToken);

        using var response = await client.PostJsonAsync($"/api/v1/instrument-versions/{version.Id.Value}/rule-sets", body, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("questionnaires.rule_set_version_exists", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task Approving_a_proposed_rule_records_the_approver_and_cannot_happen_twice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await factory.ApprovedVersionAsync(cancellationToken);
        using var client = factory.AsSystemAdministrator();
        var proposed = await ProposeAsync(client, version.Id.Value, Eligibility(new { ageAtLeast = 60 }, TestData.UniqueCode("rules")), cancellationToken);

        using var first = await client.PostJsonAsync(
            $"/api/v1/rule-sets/{proposed.Id}/approve", new { approvalSource = "Synthetic test approval" }, cancellationToken);
        using var second = await client.PostJsonAsync(
            $"/api/v1/rule-sets/{proposed.Id}/approve", new { approvalSource = "Synthetic test approval" }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var approved = await first.ReadAsync<RuleSetResponse>(cancellationToken);
        Assert.Equal((RuleSetStatus.Approved, ApiClients.SystemAdministratorSubject), (approved.Status, approved.ApprovedBy));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("questionnaires.rule_set_not_proposed", await second.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task A_rule_for_a_draft_version_cannot_be_approved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var draft = await factory.DraftVersionAsync(cancellationToken);
        using var client = factory.AsSystemAdministrator();
        var proposed = await ProposeAsync(client, draft.Id.Value, Eligibility(new { ageAtLeast = 60 }, TestData.UniqueCode("rules")), cancellationToken);

        using var response = await client.PostJsonAsync(
            $"/api/v1/rule-sets/{proposed.Id}/approve", new { approvalSource = "Synthetic test approval" }, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("questionnaires.version_not_approved", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task An_unknown_rule_set_returns_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();

        using var response = await client.GetAsync($"/api/v1/rule-sets/{Guid.NewGuid()}", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("questionnaires.rule_set_not_found", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task A_proposed_rule_set_can_be_read_with_its_definition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await factory.ApprovedVersionAsync(cancellationToken);
        using var client = factory.AsSystemAdministrator();
        var code = TestData.UniqueCode("rules");
        var proposed = await ProposeAsync(client, version.Id.Value, Eligibility(new { ageAtLeast = 60 }, code), cancellationToken);

        using var response = await client.GetAsync($"/api/v1/rule-sets/{proposed.Id}", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var read = await response.ReadAsync<RuleSetResponse>(cancellationToken);
        Assert.Equal(
            (proposed.Id, RuleSetKind.Eligibility, code, 1, version.Id.Value, RuleSetStatus.Proposed),
            (read.Id, read.Kind, read.Code, read.VersionNumber, read.InstrumentVersionId, read.Status));
        Assert.Null(read.ApprovedBy);
        // jsonb does not keep key order, so compare structure rather than text.
        using var posted = JsonDocument.Parse("""{"schema":"eligibility.v1","target":{"sectionCode":"A"},"rule":{"ageAtLeast":60}}""");
        Assert.True(JsonElement.DeepEquals(posted.RootElement, read.Definition), read.Definition.GetRawText());
    }

    [Fact]
    public async Task Approving_an_unknown_rule_set_returns_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync(
            $"/api/v1/rule-sets/{Guid.NewGuid()}/approve", new { approvalSource = "Synthetic test approval" }, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("questionnaires.rule_set_not_found", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task Approving_without_an_approval_source_returns_400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await factory.ApprovedVersionAsync(cancellationToken);
        using var client = factory.AsSystemAdministrator();
        var proposed = await ProposeAsync(client, version.Id.Value, Eligibility(new { ageAtLeast = 60 }, TestData.UniqueCode("rules")), cancellationToken);

        using var response = await client.PostJsonAsync($"/api/v1/rule-sets/{proposed.Id}/approve", new { approvalSource = "" }, cancellationToken);

        await AssertValidationKeyAsync(response, "approvalSource", cancellationToken);
    }

    [Fact]
    public async Task Approval_validates_the_definition_again_against_the_version_as_it_now_is()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var draft = await factory.DraftVersionAsync(cancellationToken);
        using var client = factory.AsSystemAdministrator();
        var scoring = new
        {
            kind = "scoring",
            code = TestData.UniqueCode("rules"),
            versionNumber = 1,
            definition = new
            {
                schema = "scoring.v1",
                target = new { sectionCode = "A" },
                policy = "summed_item_values",
                algorithmVersion = 1,
                parameters = new { reverseScoring = "mirror_scale", unanswered = "reject", total = true },
            },
        };
        var proposed = await ProposeAsync(client, draft.Id.Value, scoring, cancellationToken);
        Assert.Equal(RuleSetStatus.Proposed, proposed.Status);

        // The draft changes after the proposal: section A gains a scored item with no stated direction, then the version is approved.
        var instrument = await factory.InstrumentOfAsync(draft, cancellationToken);
        await factory.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IInstrumentVersionRepository>().GetAsync(draft.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The draft version was not saved.");
                var section = tracked.Sections.Single(candidate => candidate.Code == "A");
                var item = TestData.Ok(tracked.AddItem(
                    section.Id, "A3", 3, ItemType.SingleChoice, true, null, ScoringDirection.Unspecified,
                    TestData.Text("سؤال", "Question"), TestData.Now));
                for (var value = 1; value <= 3; value++)
                {
                    TestData.Ok(tracked.AddOption(item.Id, $"o{value}", value, value, TestData.Text("خيار", "Option"), TestData.Now));
                }
            },
            cancellationToken);
        await factory.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IInstrumentVersionRepository>().GetAsync(draft.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The draft version was not saved.");
                TestData.Ok(tracked.Approve(instrument, TestData.TestApproval(), TestData.Now.AddDays(-1), null, TestData.Now));
            },
            cancellationToken);

        using var response = await client.PostJsonAsync(
            $"/api/v1/rule-sets/{proposed.Id}/approve", new { approvalSource = "Synthetic test approval" }, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("scoring.item_direction_unspecified", await response.ProblemCodeAsync(cancellationToken));
        using var read = await client.GetAsync($"/api/v1/rule-sets/{proposed.Id}", cancellationToken);
        Assert.Equal(RuleSetStatus.Proposed, (await read.ReadAsync<RuleSetResponse>(cancellationToken)).Status);
    }

    private const string WholeVersionScoring =
        """{"schema":"scoring.v1","policy":"summed_item_values","algorithmVersion":1,"parameters":{"reverseScoring":"mirror_scale","unanswered":"reject","total":true}}""";

    private async Task<RuleSet> ApproveScoringDirectlyAsync(InstrumentVersion version, CancellationToken cancellationToken)
    {
        var rules = TestData.Ok(RuleSet.Propose(RuleSetKind.Scoring, TestData.UniqueCode("scoring"), 1, version, WholeVersionScoring, TestData.Now));
        TestData.Ok(rules.Approve(TestData.TestApproval(), version, TestData.Now));
        await factory.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IRuleSetRepository>().Add(rules);
                return Task.CompletedTask;
            },
            cancellationToken);
        return rules;
    }

    private static async Task<RuleSetResponse> ProposeAndApproveAsync(
        HttpClient client, Guid versionId, CancellationToken cancellationToken)
    {
        var proposed = await ProposeAsync(client, versionId, Eligibility(new { ageAtLeast = 60 }, TestData.UniqueCode("rules")), cancellationToken);
        using var approved = await client.PostJsonAsync(
            $"/api/v1/rule-sets/{proposed.Id}/approve", new { approvalSource = "Synthetic test approval" }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        return proposed;
    }

    [Fact]
    public async Task Retiring_an_approved_rule_set_returns_200_retired_and_writes_one_audit_event()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await factory.ApprovedVersionAsync(cancellationToken);
        using var client = factory.AsSystemAdministrator();
        var approved = await ProposeAndApproveAsync(client, version.Id.Value, cancellationToken);

        using var response = await client.PostAsync($"/api/v1/rule-sets/{approved.Id}/retire", null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var retired = await response.ReadAsync<RuleSetResponse>(cancellationToken);
        Assert.Equal((RuleSetStatus.Retired, (DateTimeOffset?)TestData.Now), (retired.Status, retired.RetiredAt));
        var events = await factory.QueryAsync(db => db.AuditEvents
            .Where(auditEvent => auditEvent.ResourceId == approved.Id.ToString())
            .ToListAsync(cancellationToken));
        var retiredEvent = Assert.Single(events, auditEvent => auditEvent.Action == "questionnaires.rule_set.retired");
        Assert.Equal("rule_set", retiredEvent.ResourceType);
        Assert.Equal(ApiClients.SystemAdministratorSubject, retiredEvent.ActorId);
    }

    [Fact]
    public async Task Retiring_an_unknown_rule_set_returns_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostAsync($"/api/v1/rule-sets/{Guid.NewGuid()}/retire", null, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("questionnaires.rule_set_not_found", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task Retiring_a_proposed_rule_set_returns_409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await factory.ApprovedVersionAsync(cancellationToken);
        using var client = factory.AsSystemAdministrator();
        var proposed = await ProposeAsync(client, version.Id.Value, Eligibility(new { ageAtLeast = 60 }, TestData.UniqueCode("rules")), cancellationToken);

        using var response = await client.PostAsync($"/api/v1/rule-sets/{proposed.Id}/retire", null, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("questionnaires.rule_set_not_approved", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task Retiring_an_already_retired_rule_set_returns_409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await factory.ApprovedVersionAsync(cancellationToken);
        using var client = factory.AsSystemAdministrator();
        var approved = await ProposeAndApproveAsync(client, version.Id.Value, cancellationToken);
        using var first = await client.PostAsync($"/api/v1/rule-sets/{approved.Id}/retire", null, cancellationToken);

        using var second = await client.PostAsync($"/api/v1/rule-sets/{approved.Id}/retire", null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("questionnaires.rule_set_not_approved", await second.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task Retiring_one_of_two_approved_scoring_rule_sets_lets_scoring_succeed_with_the_other()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, version, _, _) = await factory.SubmittedSessionAsync(cancellationToken);
        var mistaken = await ApproveScoringDirectlyAsync(version, cancellationToken);
        var kept = await ApproveScoringDirectlyAsync(version, cancellationToken);
        using var client = factory.AsSystemAdministrator();
        using var ambiguous = await client.PostJsonAsync($"/api/v1/assessment-sessions/{session.Id.Value}/score", new { }, cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, ambiguous.StatusCode);
        Assert.Equal("scoring.ambiguous_rules", await ambiguous.ProblemCodeAsync(cancellationToken));

        using var retire = await client.PostAsync($"/api/v1/rule-sets/{mistaken.Id.Value}/retire", null, cancellationToken);
        using var scored = await client.PostJsonAsync($"/api/v1/assessment-sessions/{session.Id.Value}/score", new { }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, retire.StatusCode);
        Assert.Equal(HttpStatusCode.Created, scored.StatusCode);
        var result = await scored.ReadAsync<ScoreResultResponse>(cancellationToken);
        Assert.Equal(kept.Id.Value, result.RuleSetId);
    }
}
