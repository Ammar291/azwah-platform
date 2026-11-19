using FamilyResilience.Application.Analytics;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Consent;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Persistence;

/// <summary>Research-use consent decides at the moment an export is read, not when it was requested.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ExportQueriesTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task A_member_who_withdraws_research_consent_drops_out_of_the_next_read()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, _, assignment, member) = await fixture.SubmittedSessionAsync(cancellationToken);
        var consent = await fixture.GrantedConsentAsync(
            member, await fixture.ApprovedPolicyAsync(ConsentType.ResearchUse, cancellationToken), cancellationToken);
        var filter = new ExportFilter(assignment.WaveId, null);

        var before = await fixture.InScopeAsync(
            services => services.GetRequiredService<IExportQueries>().SessionsAsync(filter, 10, cancellationToken), cancellationToken);
        await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IConsentRecordRepository>().GetAsync(consent.Id, cancellationToken);
                TestData.Ok(tracked!.Withdraw("synthetic-participant", TestData.Now.AddMinutes(5)));
            },
            cancellationToken);
        var after = await fixture.InScopeAsync(
            services => services.GetRequiredService<IExportQueries>().SessionsAsync(filter, 10, cancellationToken), cancellationToken);

        Assert.Equal(member.Id, Assert.Single(before).MemberId);
        Assert.Empty(after);
    }

    /// <summary>Half of the scored items may be answered 99; a total with excluded answers is the prorated mean, to a whole number.</summary>
    private const string ProratedScoring =
        """{"schema":"scoring.v1","policy":"summed_item_values","algorithmVersion":1,"parameters":{"reverseScoring":"mirror_scale","unanswered":"reject","nonSubstantive":{"maxShare":0.5,"aggregation":"prorated_mean","rounding":{"mode":"half_away_from_zero","decimals":0}},"total":true}}""";

    /// <summary>An approved synthetic version: one section of four required items, each with options 1–3 and a non-substantive 99.</summary>
    private async Task<InstrumentVersion> VersionOfferingPreferNotToAnswerAsync(CancellationToken cancellationToken)
    {
        var instrument = TestData.NewInstrument();
        var draft = TestData.Ok(InstrumentVersion.CreateDraft(instrument, 1, TestData.Now));
        TestData.Ok(draft.SetContentReferences("test-source (ar)", "test-source (en)", TestData.Now));
        var section = TestData.Ok(draft.AddSection("A", TestData.Text("قسم", "Section"), 1, TestData.Now));
        for (var order = 1; order <= 4; order++)
        {
            var item = TestData.Ok(draft.AddItem(
                section.Id, $"A{order}", order, ItemType.SingleChoice, true, null, ScoringDirection.Forward, TestData.Text("سؤال", "Question"), TestData.Now));
            for (var value = 1; value <= 3; value++)
            {
                TestData.Ok(draft.AddOption(item.Id, $"o{value}", value, value, TestData.Text("خيار", "Option"), TestData.Now));
            }

            TestData.Ok(draft.AddOption(
                item.Id, "o99", 4, 99, TestData.Text("يفضل عدم الإجابة", "Prefer not to answer"), TestData.Now, isNonSubstantive: true));
        }

        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IInstrumentRepository>().Add(instrument);
                services.GetRequiredService<IInstrumentVersionRepository>().Add(draft);
                return Task.CompletedTask;
            },
            cancellationToken);

        // Draft content and its approval are separate commits (the content trigger reads the committed status).
        return await fixture.InScopeAsync(
            async services =>
            {
                var version = await services.GetRequiredService<IInstrumentVersionRepository>().GetAsync(draft.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The draft version was not saved.");
                TestData.Ok(version.Approve(instrument, TestData.TestApproval(), TestData.Now.AddDays(-1), null, TestData.Now));
                return version;
            },
            cancellationToken);
    }

    /// <summary>
    /// A consenting member who answers the four items with <paramref name="values"/> (99 is "prefer not to answer"),
    /// submits and is scored by the approved rule set, each step in its own commit.
    /// </summary>
    private async Task<FamilyMemberId> ScoredMemberAsync(
        InstrumentVersion version, Wave wave, ConsentPolicyVersion research, int[] values, CancellationToken cancellationToken)
    {
        var member = (await fixture.SavedHouseholdAsync(cancellationToken)).Members[0];
        await fixture.GrantedConsentAsync(member, research, cancellationToken);
        var assignment = await fixture.AssignmentAsync(member, version, cancellationToken, wave: wave);
        var items = version.GetItems().ToList();
        var sessionId = await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IAssessmentAssignmentRepository>().GetAsync(assignment.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The assignment was not saved.");
                var started = TestData.Ok(AssessmentSession.Start(tracked, TestData.Now));
                for (var index = 0; index < items.Count; index++)
                {
                    var option = items[index].Options.Single(candidate => candidate.Value == values[index]);
                    TestData.Ok(started.Answer(version, items[index].Id, option.Id, TestData.Now));
                }

                services.GetRequiredService<IAssessmentSessionRepository>().Add(started);
                return started.Id;
            },
            cancellationToken);
        await fixture.InScopeAsync(
            async services =>
            {
                var session = await services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(sessionId, cancellationToken)
                    ?? throw new InvalidOperationException("The session was not saved.");
                var tracked = await services.GetRequiredService<IAssessmentAssignmentRepository>().GetAsync(assignment.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The assignment was not saved.");
                TestData.Ok(session.Submit(version, tracked, TestData.Now));
            },
            cancellationToken);
        await using var scope = fixture.CreateScope();
        TestData.Ok(await scope.ServiceProvider.GetRequiredService<ScoreSession>().HandleAsync(sessionId.Value, cancellationToken));
        return member.Id;
    }

    [Fact]
    public async Task An_export_tells_prefer_not_to_answer_from_a_substantive_answer_and_a_prorated_total_from_a_full_one()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await VersionOfferingPreferNotToAnswerAsync(cancellationToken);
        var rules = TestData.Ok(RuleSet.Propose(RuleSetKind.Scoring, TestData.UniqueCode("scoring"), 1, version, ProratedScoring, TestData.Now));
        TestData.Ok(rules.Approve(TestData.TestApproval(), version, TestData.Now));
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IRuleSetRepository>().Add(rules);
                return Task.CompletedTask;
            },
            cancellationToken);
        var wave = await fixture.OpenWaveAsync(cancellationToken);
        var research = await fixture.ApprovedPolicyAsync(ConsentType.ResearchUse, cancellationToken);
        var full = await ScoredMemberAsync(version, wave, research, [1, 2, 3, 3], cancellationToken);
        var prorated = await ScoredMemberAsync(version, wave, research, [99, 99, 2, 3], cancellationToken);
        var declined = await ScoredMemberAsync(version, wave, research, [99, 99, 99, 99], cancellationToken);
        var filter = new ExportFilter(wave.Id, null);

        var sessions = await fixture.InScopeAsync(
            services => services.GetRequiredService<IExportQueries>().SessionsAsync(filter, 10, cancellationToken), cancellationToken);
        var scores = await fixture.InScopeAsync(
            services => services.GetRequiredService<IExportQueries>().ScoresAsync(filter, 10, cancellationToken), cancellationToken);

        Assert.Equal(
            new Dictionary<FamilyMemberId, (int Answered, int NonSubstantive)> { [full] = (4, 0), [prorated] = (4, 2), [declined] = (4, 4) },
            sessions.ToDictionary(row => row.MemberId, row => (row.AnsweredCount, row.NonSubstantiveCount)));
        using var pseudonyms = new Pseudonymizer();
        string[] columns = ["score_status", "incomplete_reason", "total_score", "excluded_count"];
        var exported = scores.ToDictionary(row => row.MemberId, row => ExportCatalog.Project(row, columns, pseudonyms));
        // 1 + 2 + 3 + 3 = 9; (2 + 3) / 2 × 4 = 10 with two answers excluded; four of four excluded is over the half allowed.
        Assert.Equal(["complete", null, "9", "0"], exported[full]);
        Assert.Equal(["complete", null, "10", "2"], exported[prorated]);
        Assert.Equal(["incomplete", ScoreIncompleteReasons.NonSubstantiveShareExceeded, null, "4"], exported[declined]);
    }
}
