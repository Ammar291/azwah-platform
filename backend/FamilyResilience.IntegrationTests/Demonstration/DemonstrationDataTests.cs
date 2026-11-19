using System.Net;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Analytics;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.Infrastructure.Content;
using FamilyResilience.Infrastructure.Persistence.Seeding;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.IntegrationTests.Demonstration;

/// <summary>What the demonstration database holds (D-140), read through the dashboard's own endpoints where they exist.</summary>
[Collection(DemonstrationApiCollection.Name)]
public sealed class DemonstrationDataTests(DemonstrationApi api)
{
    private static readonly string[] Tiers = ["flourishing", "transitional", "vulnerable"];

    [Fact]
    public async Task Only_the_household_assessment_its_scoring_the_demonstration_rule_and_consent_placeholders_are_approved_and_each_is_labelled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var versions = await api.QueryAsync(db => db.InstrumentVersions.AsNoTracking()
            .Join(db.Instruments, version => version.InstrumentId, instrument => instrument.Id, (version, instrument) => new { instrument.Code, version })
            .ToListAsync(cancellationToken));
        var ruleSets = await api.QueryAsync(db => db.RuleSets.AsNoTracking().ToListAsync(cancellationToken));
        var policies = await api.QueryAsync(db => db.ConsentPolicyVersions.AsNoTracking().ToListAsync(cancellationToken));

        var assessment = Assert.Single(versions, row => row.Code == "household_assessment").version;
        Assert.Equal(
            (InstrumentVersionStatus.Approved, DemonstrationDataSeeder.SeedSubject, DemonstrationDataSeeder.ApprovalReference),
            (assessment.Status, assessment.ApprovedBy, assessment.ApprovalSource));
        Assert.Equal("LOCAL DEMONSTRATION ONLY (D-140): not approved by a research owner", DemonstrationDataSeeder.ApprovalReference);
        Assert.Equal(InstrumentVersionStatus.Draft, Assert.Single(versions, row => row.Code == "abu_dhabi_family_survey").version.Status);

        var approved = ruleSets.Where(ruleSet => ruleSet.Status == RuleSetStatus.Approved).OrderBy(ruleSet => ruleSet.Code, StringComparer.Ordinal).ToList();
        Assert.Equal(
            [(RuleSetKind.Eligibility, "demo_household_assessment_head"), (RuleSetKind.Scoring, "household_assessment_total")],
            approved.Select(ruleSet => (ruleSet.Kind, ruleSet.Code)));
        Assert.All(approved, ruleSet =>
        {
            Assert.Equal(assessment.Id, ruleSet.InstrumentVersionId);
            Assert.Equal((DemonstrationDataSeeder.SeedSubject, DemonstrationDataSeeder.ApprovalReference), (ruleSet.ApprovedBy, ruleSet.ApprovalSource));
        });
        Assert.StartsWith("demo_", DemonstrationDataSeeder.EligibilityRuleSetCode, StringComparison.Ordinal);
        Assert.Equal(
            ["family_voices_module_a", "family_voices_module_b", "family_voices_module_c"],
            ruleSets.Where(ruleSet => ruleSet.Status == RuleSetStatus.Proposed).Select(ruleSet => ruleSet.Code).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(ruleSets, ruleSet => ruleSet.Kind == RuleSetKind.Referral);

        Assert.Equal(Enum.GetValues<ConsentType>().Order(), policies.Select(policy => policy.ConsentType).Order());
        Assert.All(policies, policy =>
        {
            Assert.Equal(ConsentPolicyStatus.Approved, policy.Status);
            Assert.Equal((DemonstrationDataSeeder.ConsentDocumentReference, DemonstrationDataSeeder.ConsentDocumentReference),
                (policy.DocumentReferenceEn, policy.DocumentReferenceAr));
            Assert.Equal((DemonstrationDataSeeder.SeedSubject, DemonstrationDataSeeder.ApprovalReference), (policy.ApprovedBy, policy.ApprovalSource));
        });

        Assert.Equal(0, await api.QueryAsync(db => db.PartnerOrganisations.CountAsync(cancellationToken)));
        Assert.Equal(0, await api.QueryAsync(db => db.SupportResources.CountAsync(cancellationToken)));
        Assert.Equal(0, await api.QueryAsync(db => db.SupportReferrals.CountAsync(cancellationToken)));
    }

    /// <summary>The numbers in backend/README.md ("What it holds"), as the build wrote them, so the README cannot drift.</summary>
    [Fact]
    public void The_build_holds_exactly_the_households_members_assignments_sessions_and_results_the_readme_documents()
    {
        IReadOnlyDictionary<string, int> documented = new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
            ["households"] = 60,
            ["households.active"] = 58,
            ["households.withdrawn"] = 2,
            ["households.roster_confirmed"] = 59,
            ["members"] = 205,
            ["demo_wave_1.assignments"] = 55,
            ["demo_wave_1.assignments.completed"] = 47,
            ["demo_wave_1.assignments.expired"] = 8,
            ["demo_wave_1.sessions"] = 49,
            ["demo_wave_1.sessions.scored"] = 47,
            ["demo_wave_1.sessions.abandoned"] = 2,
            ["demo_wave_1.results"] = 47,
            ["demo_wave_2.assignments"] = 58,
            ["demo_wave_2.assignments.completed"] = 40,
            ["demo_wave_2.assignments.in_progress"] = 8,
            ["demo_wave_2.assignments.assigned"] = 9,
            ["demo_wave_2.assignments.cancelled"] = 1,
            ["demo_wave_2.sessions"] = 48,
            ["demo_wave_2.sessions.scored"] = 40,
            ["demo_wave_2.sessions.in_progress"] = 8,
            ["demo_wave_2.results"] = 40,
        };

        Assert.Equal(documented, api.AsBuilt);
    }

    /// <summary>
    /// Maintenance writes <c>assignments.assignment.expired</c> and <c>sessions.session.abandoned</c>, and a manager's
    /// withdrawal <c>households.household.withdrawn</c>; the build writes the same events for the same rows at the same
    /// times, with the demonstration seed as the actor, as on its approvals.
    /// </summary>
    [Fact]
    public async Task Wave_one_expiries_abandonments_and_withdrawals_are_audited_as_the_platform_audits_them_by_the_seed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        IReadOnlyCollection<string> actions = ["assignments.assignment.expired", "sessions.session.abandoned", "households.household.withdrawn"];
        var audit = await api.QueryAsync(db => db.AuditEvents.AsNoTracking()
            .Where(candidate => actions.Contains(candidate.Action))
            .ToListAsync(cancellationToken));
        var expired = await api.QueryAsync(db => db.AssessmentAssignments.AsNoTracking()
            .Where(assignment => assignment.Status == AssignmentStatus.Expired)
            .Select(assignment => new { assignment.Id, assignment.UpdatedAt })
            .ToListAsync(cancellationToken));
        var abandoned = await api.QueryAsync(db => db.AssessmentSessions.AsNoTracking()
            .Where(session => session.Status == SessionStatus.Abandoned)
            .Select(session => new { session.Id, session.AbandonedAt })
            .ToListAsync(cancellationToken));
        var withdrawn = await api.QueryAsync(db => db.Households.AsNoTracking()
            .Where(household => household.Status == HouseholdStatus.Withdrawn)
            .Select(household => new { household.Id, household.UpdatedAt })
            .ToListAsync(cancellationToken));

        IEnumerable<(string Type, string? Id, DateTimeOffset? At)> Events(string action) => audit
            .Where(candidate => candidate.Action == action)
            .Select(candidate => (candidate.ResourceType, candidate.ResourceId, (DateTimeOffset?)candidate.OccurredAt))
            .Order();
        static IEnumerable<(string Type, string? Id, DateTimeOffset? At)> Rows(string type, IEnumerable<(Guid Id, DateTimeOffset? At)> rows) =>
            rows.Select(row => (type, (string?)row.Id.ToString(), row.At)).Order();

        Assert.Equal(
            Rows("assessment_assignment", expired.Select(row => (row.Id.Value, (DateTimeOffset?)row.UpdatedAt))),
            Events("assignments.assignment.expired"));
        Assert.Equal(
            Rows("assessment_session", abandoned.Select(row => (row.Id.Value, row.AbandonedAt))),
            Events("sessions.session.abandoned"));
        Assert.Equal(
            Rows("household", withdrawn.Select(row => (row.Id.Value, (DateTimeOffset?)row.UpdatedAt))),
            Events("households.household.withdrawn"));
        Assert.Equal((8, 2, 2), (expired.Count, abandoned.Count, withdrawn.Count));
        Assert.All(audit, candidate => Assert.Equal(
            (AuditActorType.User, DemonstrationDataSeeder.SeedSubject, AuditOutcome.Succeeded),
            (candidate.ActorType, candidate.ActorId, candidate.Outcome)));
    }

    [Fact]
    public async Task Wave_one_closed_about_four_months_ago_and_wave_two_is_the_only_open_wave()
    {
        var now = TestData.Now;

        Assert.Equal(WaveStatus.Closed, api.WaveOne.Status);
        Assert.InRange((now - api.WaveOne.OpensAt).TotalDays, 200, 220);
        Assert.InRange((now - api.WaveOne.ClosesAt).TotalDays, 110, 130);
        Assert.Equal(WaveStatus.Open, api.WaveTwo.Status);
        Assert.InRange((now - api.WaveTwo.OpensAt).TotalDays, 18, 24);
        Assert.InRange((api.WaveTwo.ClosesAt - now).TotalDays, 63, 77);
    }

    [Fact]
    public async Task Each_wave_shows_all_three_tiers_unsuppressed_with_at_least_ten_people_each()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var researcher = api.As("dev-researcher", Roles.Researcher);

        foreach (var wave in new[] { api.WaveOne, api.WaveTwo })
        {
            using var response = await researcher.GetAsync($"/api/v1/admin/analytics/score-distributions?waveId={wave.Id.Value}", cancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var distribution = Assert.Single((await response.ReadAsync<ScoreDistributionsResponse>(cancellationToken)).Distributions);
            Assert.Equal(("household_assessment", 1, (string?)null, "household_assessment_total", false),
                (distribution.InstrumentCode, distribution.VersionNumber, distribution.SectionCode, distribution.RuleSetCode, distribution.Suppressed));
            Assert.Equal(Tiers, distribution.Tiers.Select(tier => tier.Tier));
            Assert.All(distribution.Tiers, tier =>
            {
                Assert.False(tier.Suppressed);
                Assert.True(tier.Count >= 10, $"{wave.Code} {tier.Tier}: {tier.Count}");
            });
        }
    }

    [Fact]
    public async Task The_overview_shows_every_assignment_state_a_completion_trend_and_unsuppressed_age_and_employment_groups()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var administrator = api.As("dev-program-administrator", Roles.ProgramAdministrator);

        async Task<AnalyticsOverviewResponse> OverviewAsync(Wave wave)
        {
            using var response = await administrator.GetAsync($"/api/v1/admin/analytics/overview?waveId={wave.Id.Value}", cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadAsync<AnalyticsOverviewResponse>(cancellationToken);
        }

        var waveOne = await OverviewAsync(api.WaveOne);
        var waveTwo = await OverviewAsync(api.WaveTwo);

        // One household enrolled without confirming its roster (another test may enrol the new-participant persona).
        Assert.Equal((2, waveOne.Enrollment.Households - 1), (waveOne.Enrollment.WithdrawnHouseholds, waveOne.Enrollment.RosterConfirmedHouseholds));
        Assert.True(waveOne.Assignments is { Completed: > 0, Expired: > 0, Assigned: 0, InProgress: 0 }, waveOne.Assignments.ToString());
        Assert.True(waveTwo.Assignments is { Completed: > 0, InProgress: > 0, Assigned: > 0, Cancelled: 1, Expired: 0 }, waveTwo.Assignments.ToString());
        Assert.True(waveOne.CompletionTrend.Count(point => point.Completed > 0) >= 3, "Wave one's completions are spread over its months.");
        foreach (var overview in new[] { waveOne, waveTwo })
        {
            var ages = overview.Demographics.ByAgeBand.Where(row => row.Total > 0 || row.Suppressed).ToList();
            Assert.Equal(["30_44", "45_59", "60_plus"], ages.Select(row => row.Group));
            Assert.All(ages, row => Assert.False(row.Suppressed, row.Group));
            var employment = overview.Demographics.ByEmploymentStatus.Where(row => row.Total > 0 || row.Suppressed).ToList();
            Assert.Equal(["employed", "self_employed", "retired"], employment.Select(row => row.Group));
            Assert.All(employment, row => Assert.False(row.Suppressed, row.Group));
        }
    }

    [Fact]
    public async Task Monitoring_lists_assignments_in_every_state_and_households_in_every_participation_state()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var administrator = api.As("dev-program-administrator", Roles.ProgramAdministrator);

        foreach (var status in new[] { "assigned", "in_progress", "completed", "expired", "cancelled" })
        {
            using var response = await administrator.GetAsync($"/api/v1/admin/assessments?status={status}", cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True((await response.ReadAsync<PagedResponse<AssessmentRowResponse>>(cancellationToken)).TotalCount > 0, status);
        }

        foreach (var query in new[] { "status=active", "status=withdrawn", "rosterConfirmed=true", "rosterConfirmed=false" })
        {
            using var response = await administrator.GetAsync($"/api/v1/admin/participation?{query}", cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True((await response.ReadAsync<PagedResponse<ParticipationRowResponse>>(cancellationToken)).TotalCount > 0, query);
        }

        var sessions = await api.QueryAsync(db => db.AssessmentSessions.AsNoTracking().Select(session => session.Status).Distinct().ToListAsync(cancellationToken));
        Assert.Equal([SessionStatus.InProgress, SessionStatus.Scored, SessionStatus.Abandoned], sessions.Order());
    }

    [Fact]
    public async Task System_administrators_see_household_results_of_both_waves()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var administrator = api.As("dev-system-administrator", Roles.SystemAdministrator);

        foreach (var wave in new[] { api.WaveOne, api.WaveTwo })
        {
            using var response = await administrator.GetAsync($"/api/v1/admin/results?waveId={wave.Id.Value}&pageSize=100", cancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = await response.ReadAsync<PagedResponse<HouseholdResultRowResponse>>(cancellationToken);
            Assert.True(page.TotalCount >= 30, $"{wave.Code}: {page.TotalCount}");
            Assert.All(page.Items, row =>
            {
                Assert.Equal((ScoreStatus.Complete, "household_assessment"), (row.State, row.InstrumentCode));
                Assert.Contains(row.TierCode, Tiers);
                Assert.StartsWith("HH-DM", row.HouseholdReference, StringComparison.Ordinal);
            });
            Assert.Equal(Tiers, page.Items.Select(row => row.TierCode).Distinct().Order(StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// The expectation is computed here from the content file and the stored raw answers, never by the engine: a forward
    /// item counts its raw value, a reverse item 5 − raw, and the tier is the band of the committed scoring file.
    /// </summary>
    [Fact]
    public async Task Every_stored_total_equals_a_reference_sum_of_the_stored_answers_in_the_content_files_directions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var package = ContentFiles.ReadPackage(ContentFiles.HouseholdAssessment);
        var reverse = package.Version.Sections.SelectMany(section => section.Items)
            .ToDictionary(item => item.Code, item => item.ScoringDirection == ScoringDirection.Reverse, StringComparer.Ordinal);
        Assert.Equal(10, reverse.Count(item => item.Value));

        var itemCodes = await api.QueryAsync(db => db.Set<InstrumentItem>().AsNoTracking()
            .ToDictionaryAsync(item => item.Id, item => item.Code, cancellationToken));
        var results = await api.QueryAsync(db => db.ScoreResults.AsNoTracking()
            .Select(result => new { result.AssessmentSessionId, result.TotalScore, result.TierCode, result.Status })
            .ToListAsync(cancellationToken));
        var sessions = await api.QueryAsync(db => db.AssessmentSessions.AsNoTracking().Include(session => session.Responses)
            .Where(session => session.Status == SessionStatus.Scored)
            .ToDictionaryAsync(session => session.Id, cancellationToken));

        Assert.True(results.Count >= 80, $"{results.Count} results");
        foreach (var result in results)
        {
            var responses = sessions[result.AssessmentSessionId].Responses;
            Assert.Equal(39, responses.Count);
            var reference = responses.Sum(response => reverse[itemCodes[response.InstrumentItemId]] ? 5 - response.RawValue : response.RawValue);
            Assert.Equal((ScoreStatus.Complete, (decimal?)reference), (result.Status, result.TotalScore));
            Assert.Equal(reference switch { <= 78 => "vulnerable", <= 117 => "transitional", _ => "flourishing" }, result.TierCode);
        }
    }

    [Fact]
    public async Task Building_again_changes_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var before = await DemonstrationApi.RowCountsAsync(api.DemonstrationConnectionString, cancellationToken);

        Assert.Equal(0, await api.RunDemoCommandAsync(["db", "demo"], cancellationToken));

        Assert.Equal(before, await DemonstrationApi.RowCountsAsync(api.DemonstrationConnectionString, cancellationToken));
        Assert.True(before["households"] >= 60);
    }

    [Fact]
    public async Task The_ordinary_database_the_command_was_configured_with_is_left_untouched()
    {
        var counts = await DemonstrationApi.RowCountsAsync(api.OrdinaryConnectionString, TestContext.Current.CancellationToken);

        Assert.Empty(counts);
    }
}
