using System.Net;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Analytics;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.Infrastructure.Persistence.Seeding;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using IdentityProviderPersonas = FamilyResilience.IdentityProvider.Personas;

namespace FamilyResilience.IntegrationTests.Demonstration;

/// <summary>
/// The development identity provider's personas against the demonstration database, through the real participant and
/// staff endpoints: the lead answers the open wave live, the member sees no score, the new participant enrols.
/// </summary>
[Collection(DemonstrationApiCollection.Name)]
public sealed class DemonstrationPersonaTests(DemonstrationApi api)
{
    [Fact]
    public void The_linked_subjects_are_the_identity_providers_participant_personas()
    {
        Assert.Equal(DemonstrationDataSeeder.LeadSubject, IdentityProviderPersonas.Find("participant_lead")!.Subject);
        Assert.Equal(DemonstrationDataSeeder.MemberSubject, IdentityProviderPersonas.Find("participant_member")!.Subject);
        Assert.Equal(DemonstrationDataSeeder.NewParticipantSubject, IdentityProviderPersonas.Find("participant_new")!.Subject);
        Assert.DoesNotContain(IdentityProviderPersonas.All, persona => persona.Subject == DemonstrationDataSeeder.SeedSubject);
    }

    [Fact]
    public async Task The_lead_persona_answers_the_open_wave_live_and_sees_the_score_while_the_dashboard_counts_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var lead = api.As(DemonstrationDataSeeder.LeadSubject, Roles.Participant);
        using var researcher = api.As("dev-researcher", Roles.Researcher);

        var me = await ReadAsync<MeResponse>(lead, "/api/v1/me", cancellationToken);
        Assert.Equal((HouseholdRole.Manager, HouseholdStatus.Active), (me.Household!.Role, me.Household.Status));
        var assignments = (await ReadAsync<PagedResponse<ParticipantAssignmentResponse>>(lead, "/api/v1/assignments?pageSize=100", cancellationToken)).Items;
        Assert.All(assignments, assignment => Assert.Equal(me.Household.MemberId, assignment.MemberId));
        Assert.Contains(assignments, assignment => assignment is { WaveCode: DemonstrationDataSeeder.WaveOneCode, Status: AssignmentStatus.Completed });
        var open = Assert.Single(assignments, assignment => assignment.WaveCode == DemonstrationDataSeeder.WaveTwoCode);
        Assert.Equal(("household_assessment", InstrumentKind.HouseholdAssessment, (string?)null, AssignmentStatus.Assigned, (Guid?)null),
            (open.InstrumentCode, open.InstrumentKind, open.SectionCode, open.Status, open.ActiveSessionId));
        var history = (await ReadAsync<PagedResponse<AssessmentHistoryItemResponse>>(lead, "/api/v1/me/assessment-history", cancellationToken)).Items;
        var earlier = Assert.Single(history);
        Assert.Equal((DemonstrationDataSeeder.WaveOneCode, SessionStatus.Scored, ScoreStatus.Complete), (earlier.WaveCode, earlier.Status, earlier.ScoreStatus));
        Assert.NotNull(earlier.TierCode);
        var countedBefore = await WaveTwoTotalAsync(researcher, cancellationToken);

        using var started = await lead.PostJsonAsync($"/api/v1/assignments/{open.Id}/sessions", new { }, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var session = await started.ReadAsync<SessionResponse>(cancellationToken);
        var version = await ReadAsync<PublishedVersionResponse>(lead, "/api/v1/questionnaires/household_assessment/versions/1", cancellationToken);
        // The third option of every item: raw 3 everywhere, which the approved rule set totals to 107 (transitional).
        var answers = version.Sections.SelectMany(section => section.Items)
            .Select(item => (object)new { itemId = item.Id, optionId = item.Options[2].Id })
            .ToArray();
        await Journeys.SaveAsync(lead, session.Id, answers, cancellationToken);
        using var submitted = await Journeys.SubmitAsync(lead, session.Id, Guid.NewGuid(), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);

        var result = await ReadAsync<SessionResultResponse>(lead, $"/api/v1/sessions/{session.Id}/result", cancellationToken);
        Assert.Equal(
            (SessionStatus.Scored, ResultState.Complete, ScoreVisibility.Visible, (decimal?)107m, "transitional"),
            (result.SessionStatus, result.State, result.ScoreVisibility, result.TotalScore, result.TierCode));
        Assert.Equal(countedBefore + 1, await WaveTwoTotalAsync(researcher, cancellationToken));
    }

    [Fact]
    public async Task The_member_persona_belongs_to_the_leads_household_but_is_not_given_the_heads_score()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var member = api.As(DemonstrationDataSeeder.MemberSubject, Roles.Participant);
        using var lead = api.As(DemonstrationDataSeeder.LeadSubject, Roles.Participant);

        var memberMe = await ReadAsync<MeResponse>(member, "/api/v1/me", cancellationToken);
        var leadMe = await ReadAsync<MeResponse>(lead, "/api/v1/me", cancellationToken);
        Assert.Equal(leadMe.Household!.HouseholdId, memberMe.Household!.HouseholdId);
        Assert.Equal(HouseholdRole.Member, memberMe.Household.Role);
        Assert.Equal([memberMe.Household.MemberId], memberMe.Household.ActableMemberIds);
        Assert.NotEqual(leadMe.Household.MemberId, memberMe.Household.MemberId);
        Assert.Empty((await ReadAsync<PagedResponse<ParticipantAssignmentResponse>>(member, "/api/v1/assignments", cancellationToken)).Items);

        var headSession = await api.QueryAsync(db => db.AssessmentSessions.AsNoTracking()
            .Where(session => session.FamilyMemberId == FamilyMemberId.From(leadMe.Household.MemberId)
                && session.Status == SessionStatus.Scored)
            .OrderBy(session => session.StartedAt)
            .Select(session => session.Id.Value)
            .FirstAsync(cancellationToken));
        using var refused = await member.GetAsync($"/api/v1/sessions/{headSession}/result", cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        var shown = await ReadAsync<SessionResultResponse>(lead, $"/api/v1/sessions/{headSession}/result", cancellationToken);
        Assert.Equal(ScoreVisibility.Visible, shown.ScoreVisibility);
        Assert.NotNull(shown.TierCode);
    }

    [Fact]
    public async Task The_new_participant_persona_enrols_as_head_and_is_assigned_the_open_wave()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var newcomer = api.As(DemonstrationDataSeeder.NewParticipantSubject, Roles.Participant);

        var before = await ReadAsync<MeResponse>(newcomer, "/api/v1/me", cancellationToken);
        Assert.Equal(((MyHouseholdResponse?)null, (DateTimeOffset?)null), (before.Household, before.RegisteredAt));

        var household = await newcomer.CreateHouseholdAsync(cancellationToken, Rosters.Head(), Rosters.Spouse(), Rosters.Child(9));
        using var confirmed = await newcomer.PostJsonAsync($"/api/v1/households/{household.Id}/confirm-roster", new { }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var confirmation = await confirmed.ReadAsync<RosterConfirmationResponse>(cancellationToken);
        var self = (await newcomer.MembersAsync(household.Id, cancellationToken)).Single(member => member.IsCaller).Id;
        Assert.Equal((api.WaveTwo.Id.Value, DemonstrationDataSeeder.WaveTwoCode), (confirmation.WaveId, confirmation.WaveCode));
        var assignment = Assert.Single(confirmation.Assignments);
        Assert.Equal((self, "household_assessment", (string?)null, AssignmentStatus.Assigned),
            (assignment.MemberId, assignment.InstrumentCode, assignment.SectionCode, assignment.Status));
    }

    /// <summary>Staff roles come from the token alone: nothing is seeded for the staff personas, and their reads work.</summary>
    [Fact]
    public async Task Staff_personas_read_the_dashboard_with_nothing_seeded_for_them()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var reads = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Roles.Researcher] = "/api/v1/admin/analytics/score-distributions",
            [Roles.ProgramAdministrator] = "/api/v1/admin/participation",
            [Roles.SystemAdministrator] = "/api/v1/admin/results",
            [Roles.SupportCoordinator] = "/api/v1/admin/referrals",
        };
        var staff = IdentityProviderPersonas.All.Where(persona => !persona.Roles.Contains(Roles.Participant)).ToList();
        Assert.Equal(reads.Keys.Order(StringComparer.Ordinal), staff.SelectMany(persona => persona.Roles).Order(StringComparer.Ordinal));

        foreach (var persona in staff)
        {
            Assert.False(await api.QueryAsync(db => db.PlatformUsers.AnyAsync(user => user.Subject == persona.Subject, cancellationToken)));
            using var client = api.As(persona.Subject, [.. persona.Roles]);
            using var response = await client.GetAsync(reads[persona.Roles.Single()], cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    private async Task<int?> WaveTwoTotalAsync(HttpClient researcher, CancellationToken cancellationToken) =>
        Assert.Single((await ReadAsync<ScoreDistributionsResponse>(
            researcher, $"/api/v1/admin/analytics/score-distributions?waveId={api.WaveTwo.Id.Value}", cancellationToken)).Distributions).Total;

    private static async Task<T> ReadAsync<T>(HttpClient client, string url, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(url, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<T>(cancellationToken);
    }
}
