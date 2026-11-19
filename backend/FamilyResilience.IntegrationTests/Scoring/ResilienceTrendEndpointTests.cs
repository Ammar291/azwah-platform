using System.Net;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Scoring;

/// <summary>
/// GET /api/v1/me/resilience-trend (D-132): the household assessment's recorded totals, oldest first, on the approved
/// scale, for the household's head only. Synthetic content: four items valued 1–3, so whole-version totals run 4–12.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ResilienceTrendEndpointTests(ApiFactory factory)
{
    private const string Url = "/api/v1/me/resilience-trend";

    private const string WholeVersionScoring =
        """{"schema":"scoring.v1","target":null,"policy":"summed_item_values","algorithmVersion":1,"parameters":{"reverseScoring":"mirror_scale","unanswered":"reject","total":true,"tierBands":[{"code":"band_low","min":4,"max":7},{"code":"band_high","min":8,"max":12}]}}""";

    private sealed record Family(HttpClient Client, string Subject, IReadOnlyList<FamilyMember> Members) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }

    /// <summary>A household enrolled straight into the database; the caller is linked to the member at <paramref name="selfIndex"/>.</summary>
    private async Task<Family> EnrolAsync(int selfIndex, params FamilyMemberDetails[] roster)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = ApiClients.NewSubject("participant");
        var user = TestData.Ok(PlatformUser.Register(subject, TestData.Now));
        var household = TestData.Ok(Household.Enrol(HouseholdReference.Generate(), roster, selfIndex, user.Id, TestData.Now));
        await factory.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IPlatformUserRepository>().Add(user);
                services.GetRequiredService<IHouseholdRepository>().Add(household);
                return Task.CompletedTask;
            },
            cancellationToken);
        var participation = await factory.ApprovedPolicyAsync(ConsentType.Participation, cancellationToken);
        foreach (var member in household.Members)
        {
            await factory.GrantedConsentAsync(member, participation, cancellationToken);
        }

        return new Family(factory.AsParticipant(subject), subject, household.Members.ToList());
    }

    /// <summary>A new approved synthetic version of a new instrument of <paramref name="kind"/>.</summary>
    private async Task<InstrumentVersion> ApprovedVersionAsync(InstrumentKind kind)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrument = TestData.NewInstrument(kind);
        await factory.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IInstrumentRepository>().Add(instrument);
                return Task.CompletedTask;
            },
            cancellationToken);
        return await factory.ApprovedVersionAsync(cancellationToken, instrument);
    }

    private async Task<RuleSet> ApproveScoringAsync(InstrumentVersion version)
    {
        var rules = TestData.Ok(RuleSet.Propose(
            RuleSetKind.Scoring, TestData.UniqueCode("trend_scoring"), 1, version, WholeVersionScoring, TestData.Now));
        TestData.Ok(rules.Approve(TestData.TestApproval(), version, TestData.Now));
        await factory.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IRuleSetRepository>().Add(rules);
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);
        return rules;
    }

    /// <summary>Assigns the version in a new wave, then answers every item with its lowest (or highest) option and submits.</summary>
    private async Task<Guid> CheckInAsync(Family family, FamilyMember member, InstrumentVersion version, bool highest = false)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var wave = await factory.OpenWaveAsync(cancellationToken);
        var assignment = await factory.AssignmentAsync(member, version, cancellationToken, wave: wave);
        var session = await Journeys.StartAsync(family.Client, assignment.Id.Value, cancellationToken);
        var answers = version.GetItems()
            .Select(item =>
            {
                var options = item.Options.OrderBy(option => option.Value).ToList();
                return (object)new { itemId = item.Id.Value, optionId = (highest ? options[^1] : options[0]).Id.Value };
            })
            .ToArray();
        await Journeys.SaveAsync(family.Client, session.Id, answers, cancellationToken);
        using var submitted = await Journeys.SubmitAsync(family.Client, session.Id, Guid.NewGuid(), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        return session.Id;
    }

    private async Task<string> WaveCodeOfAsync(Guid sessionId) =>
        await factory.QueryAsync(db => db.AssessmentSessions
            .Where(session => session.Id == Domain.Sessions.AssessmentSessionId.From(sessionId))
            .Join(db.AssessmentAssignments, session => session.AssessmentAssignmentId, assignment => assignment.Id, (_, assignment) => assignment.WaveId)
            .Join(db.Waves, waveId => waveId, wave => wave.Id, (_, wave) => wave.Code)
            .SingleAsync(TestContext.Current.CancellationToken));

    [Fact]
    public async Task The_head_sees_each_recorded_total_oldest_first_on_the_approved_scale()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var family = await EnrolAsync(0, TestData.Head());
        var head = family.Members[0];
        var assessment = await ApprovedVersionAsync(InstrumentKind.HouseholdAssessment);
        var pending = await CheckInAsync(family, head, assessment);
        await ApproveScoringAsync(assessment);
        var low = await CheckInAsync(family, head, assessment);
        var high = await CheckInAsync(family, head, assessment, highest: true);
        var voices = await ApprovedVersionAsync(InstrumentKind.FamilyVoices);
        await ApproveScoringAsync(voices);
        var module = await CheckInAsync(family, head, voices);

        using var response = await family.Client.GetAsync(Url, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var trend = await response.ReadAsync<ResilienceTrendResponse>(cancellationToken);
        Assert.Equal(ScoreVisibility.Visible, trend.ScoreVisibility);
        Assert.Equal(
            [(low, await WaveCodeOfAsync(low), 4m, "band_low", 0), (high, await WaveCodeOfAsync(high), 12m, "band_high", 0)],
            trend.Points.Select(point => (point.SessionId, point.WaveCode, point.TotalScore, point.TierCode, point.Segment)));
        Assert.All(trend.Points, point => Assert.Equal(TestData.Now, point.SubmittedAt));
        Assert.DoesNotContain(trend.Points, point => point.SessionId == pending || point.SessionId == module);
        Assert.NotNull(trend.Scale);
        Assert.Equal((4, 12), (trend.Scale.Minimum, trend.Scale.Maximum));
        Assert.Equal([("band_low", 4, 7), ("band_high", 8, 12)], trend.Scale.Bands.Select(band => (band.Code, band.Min, band.Max)));
        Assert.Equal(1, trend.NotShownCount);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        foreach (var hidden in new[] { "responses", "optionId", "memberId", "categoryScores" })
        {
            Assert.DoesNotContain(hidden, body, StringComparison.Ordinal);
        }

        var reads = await factory.QueryAsync(db => db.AuditEvents
            .Where(audit => audit.Action == "scoring.resilience_trend.read" && audit.ActorId == family.Subject)
            .ToListAsync(cancellationToken));
        Assert.Equal(("resilience_trend", (string?)null), (Assert.Single(reads).ResourceType, reads[0].ResourceId));
    }

    [Fact]
    public async Task Of_two_results_computed_at_the_same_instant_the_one_recorded_later_is_drawn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var family = await EnrolAsync(0, TestData.Head());
        var head = family.Members[0];
        var assessment = await ApprovedVersionAsync(InstrumentKind.HouseholdAssessment);
        await ApproveScoringAsync(assessment);
        var sessionId = await CheckInAsync(family, head, assessment, highest: true);
        var first = await factory.QueryAsync(db => db.ScoreResults.AsNoTracking()
            .SingleAsync(result => result.AssessmentSessionId == Domain.Sessions.AssessmentSessionId.From(sessionId), cancellationToken));
        // A session has one result per rule set, so the second comes from a second approved rule set with the same scale.
        var rules = await ApproveScoringAsync(assessment);
        var second = await factory.InScopeAsync(
            async services =>
            {
                var session = await services.GetRequiredService<IAssessmentSessionRepository>()
                    .GetAsync(Domain.Sessions.AssessmentSessionId.From(sessionId), cancellationToken)
                    ?? throw new InvalidOperationException("The session was not saved.");
                var recorded = TestData.Ok(Domain.Scoring.ScoreResult.Record(session, rules, TestData.SyntheticComputation(session, "band_low"), first.ComputedAt));
                services.GetRequiredService<IScoreResultRepository>().Add(recorded);
                return recorded;
            },
            cancellationToken);

        using var response = await family.Client.GetAsync(Url, cancellationToken);

        // The same rule as the household results (D-139): the greater version-7 identifier is the one recorded later.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(second.Id.Value.CompareTo(first.Id.Value) > 0);
        var trend = await response.ReadAsync<ResilienceTrendResponse>(cancellationToken);
        Assert.Equal([(sessionId, "band_low")], trend.Points.Select(point => (point.SessionId, point.TierCode)));
    }

    [Fact]
    public async Task A_head_without_a_recorded_total_gets_no_scale_and_no_point()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var family = await EnrolAsync(0, TestData.Head());

        using var response = await family.Client.GetAsync(Url, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var trend = await response.ReadAsync<ResilienceTrendResponse>(cancellationToken);
        Assert.Equal((ScoreVisibility.Visible, (ScoreScaleResponse?)null, 0, 0), (trend.ScoreVisibility, trend.Scale, trend.Points.Count, trend.NotShownCount));
    }

    [Fact]
    public async Task A_manager_answering_for_the_head_is_told_the_chart_is_the_heads_and_gets_no_value()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var family = await EnrolAsync(1, TestData.Head(), TestData.Spouse());
        var head = family.Members[0];
        var assessment = await ApprovedVersionAsync(InstrumentKind.HouseholdAssessment);
        await ApproveScoringAsync(assessment);
        await CheckInAsync(family, head, assessment, highest: true);

        using var response = await family.Client.GetAsync(Url, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var trend = await response.ReadAsync<ResilienceTrendResponse>(cancellationToken);
        Assert.Equal(
            (ScoreVisibility.HeadOfHouseholdOnly, (ScoreScaleResponse?)null, 0, 0),
            (trend.ScoreVisibility, trend.Scale, trend.Points.Count, trend.NotShownCount));
        Assert.Equal(1, await factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "scoring.resilience_trend.read" && audit.ActorId == family.Subject, cancellationToken)));
    }

    [Theory]
    [InlineData(Roles.Researcher)]
    [InlineData(Roles.SupportCoordinator)]
    [InlineData(Roles.ProgramAdministrator)]
    [InlineData(Roles.SystemAdministrator)]
    public async Task Staff_get_403(string role)
    {
        using var client = factory.As(ApiClients.NewSubject("staff"), role);

        using var response = await client.GetAsync(Url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        using var client = factory.As(null);

        using var response = await client.GetAsync(Url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
