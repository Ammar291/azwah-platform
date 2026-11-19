using System.Net;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Audit;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.IntegrationTests.Security;

/// <summary>
/// Administrative changes through the API each write their audit event, naming the administrator. Partners, hand-offs,
/// version approval, rule-set retirement, assignment runs, scoring and exports are asserted in their own endpoint tests;
/// these cover the rest of the content and rules workflow.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AdministrativeAuditTests(ApiFactory factory)
{
    private async Task AssertAuditedAsync(string action, Guid resourceId)
    {
        var recorded = await factory.QueryAsync(db => db.AuditEvents.AsNoTracking()
            .Where(audit => audit.Action == action && audit.ResourceId == resourceId.ToString())
            .ToListAsync(TestContext.Current.CancellationToken));
        var single = Assert.Single(recorded);
        Assert.Equal(
            (AuditActorType.User, ApiClients.SystemAdministratorSubject, AuditOutcome.Succeeded),
            (single.ActorType, single.ActorId, single.Outcome));
    }

    [Fact]
    public async Task Creating_an_instrument_is_audited()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync(
            "/api/v1/instruments",
            new { code = TestData.UniqueCode("audited"), kind = "family_voices", name = new { ar = "أداة", en = "Survey" }, description = new { ar = "وصف", en = "About" } },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await AssertAuditedAsync("questionnaires.instrument.created", (await response.ReadAsync<InstrumentResponse>(cancellationToken)).Id);
    }

    [Fact]
    public async Task Proposing_and_approving_a_rule_set_are_each_audited()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await factory.ApprovedVersionAsync(cancellationToken);
        using var client = factory.AsSystemAdministrator();

        using var proposed = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{version.Id.Value}/rule-sets",
            new
            {
                kind = "eligibility",
                code = TestData.UniqueCode("audited"),
                versionNumber = 1,
                definition = new { schema = "eligibility.v1", target = new { sectionCode = "A" }, rule = new { ageAtLeast = 60 } },
            },
            cancellationToken);
        var ruleSet = await proposed.ReadAsync<RuleSetResponse>(cancellationToken);
        using var approved = await client.PostJsonAsync(
            $"/api/v1/rule-sets/{ruleSet.Id}/approve", new { approvalSource = "Synthetic test approval" }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        await AssertAuditedAsync("questionnaires.rule_set.proposed", ruleSet.Id);
        await AssertAuditedAsync("questionnaires.rule_set.approved", ruleSet.Id);
    }

    [Fact]
    public async Task Retiring_an_instrument_version_is_audited()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await factory.ApprovedVersionAsync(cancellationToken);
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync(
            $"/api/v1/instrument-versions/{version.Id.Value}/retire", new { effectiveTo = TestData.Now }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertAuditedAsync("questionnaires.instrument_version.retired", version.Id.Value);
    }

    [Fact]
    public async Task A_refused_administrative_change_records_no_success()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = ApiClients.NewSubject("researcher");
        using var client = factory.AsResearcher(subject);

        using var response = await client.PostJsonAsync(
            "/api/v1/instruments",
            new { code = TestData.UniqueCode("refused"), kind = "family_voices", name = new { ar = "أداة", en = "Survey" }, description = new { ar = "وصف", en = "About" } },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var trail = await factory.QueryAsync(db => db.AuditEvents.AsNoTracking()
            .Where(audit => audit.ActorId == subject && audit.ResourceType != "platform_role")
            .ToListAsync(cancellationToken));
        var denied = Assert.Single(trail);
        Assert.Equal(("authorization.role.denied", "api/v1/instruments", AuditOutcome.Denied), (denied.Action, denied.ResourceId, denied.Outcome));
    }
}
