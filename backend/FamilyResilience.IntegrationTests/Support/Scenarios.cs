using System.Text.Json;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Consent;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Application.Referrals;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Referrals;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>Builds and saves synthetic aggregates through the repositories, one committed scope per step.</summary>
internal static class Scenarios
{
    public static async Task<Household> SavedHouseholdAsync(
        this IScenarioDatabase fixture, CancellationToken cancellationToken, params FamilyMemberDetails[] roster)
    {
        var household = TestData.NewHousehold(roster);
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IHouseholdRepository>().Add(household);
                return Task.CompletedTask;
            },
            cancellationToken);
        return household;
    }

    /// <summary>An approved synthetic consent policy. Only test databases ever hold approved test policies.</summary>
    public static async Task<ConsentPolicyVersion> ApprovedPolicyAsync(
        this IScenarioDatabase fixture, ConsentType type, CancellationToken cancellationToken)
    {
        var policy = TestData.Ok(ConsentPolicyVersion.CreateDraft(
            type, TestData.NextVersionNumber(), "consent/test/ar", "consent/test/en", TestData.Now));
        TestData.Ok(policy.Approve(TestData.TestApproval(), TestData.Now));
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IConsentPolicyVersionRepository>().Add(policy);
                return Task.CompletedTask;
            },
            cancellationToken);
        return policy;
    }

    public static async Task<ConsentRecord> GrantedConsentAsync(
        this IScenarioDatabase fixture, FamilyMember member, ConsentPolicyVersion policy, CancellationToken cancellationToken)
    {
        var consent = TestData.Ok(ConsentRecord.Grant(
            member, policy, ConsentCaptureMethod.InApp, FixtureCurrentUser.Subject, TestData.Now));
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IConsentRecordRepository>().Add(consent);
                return Task.CompletedTask;
            },
            cancellationToken);
        return consent;
    }

    /// <summary>Outbox messages of <paramref name="type"/> whose payload names <paramref name="id"/>.</summary>
    public static async Task<IReadOnlyList<OutboxMessage>> OutboxMessagesAsync(
        this IScenarioDatabase fixture, string type, string idProperty, Guid id, CancellationToken cancellationToken)
    {
        var messages = await fixture.QueryAsync(db =>
            db.OutboxMessages.Where(message => message.Type == type).ToListAsync(cancellationToken));
        return messages.Where(message => PayloadNames(message, idProperty, id)).ToList();
    }

    private static bool PayloadNames(OutboxMessage message, string idProperty, Guid id)
    {
        using var payload = JsonDocument.Parse(message.Payload);
        return payload.RootElement.TryGetProperty(idProperty, out var value) && value.GetGuid() == id;
    }

    /// <summary>
    /// A new instrument with one synthetic version, saved as a draft and then approved in a second commit, effective
    /// from a day before <see cref="TestData.Now"/>. Only test databases hold approved synthetic content.
    /// </summary>
    public static async Task<InstrumentVersion> ApprovedVersionAsync(
        this IScenarioDatabase fixture, CancellationToken cancellationToken, Instrument? instrument = null, int versionNumber = 1,
        DateTimeOffset? effectiveFrom = null, DateTimeOffset? effectiveTo = null)
    {
        var draft = await fixture.DraftVersionAsync(cancellationToken, instrument, versionNumber);
        var owner = await fixture.InstrumentOfAsync(draft, cancellationToken);
        return await fixture.InScopeAsync(
            async services =>
            {
                var version = await services.GetRequiredService<IInstrumentVersionRepository>().GetAsync(draft.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The draft version was not saved.");
                TestData.Ok(version.Approve(
                    owner, TestData.TestApproval(), effectiveFrom ?? TestData.Now.AddDays(-1), effectiveTo, TestData.Now));
                return version;
            },
            cancellationToken);
    }

    /// <summary>The instrument a saved version belongs to, read through a fresh context.</summary>
    public static Task<Instrument> InstrumentOfAsync(
        this IScenarioDatabase fixture, InstrumentVersion version, CancellationToken cancellationToken) =>
        fixture.QueryAsync(db => db.Instruments.SingleAsync(instrument => instrument.Id == version.InstrumentId, cancellationToken));

    /// <summary>Saves <paramref name="instrument"/> (or a new one) and a draft version of it.</summary>
    public static async Task<InstrumentVersion> DraftVersionAsync(
        this IScenarioDatabase fixture, CancellationToken cancellationToken, Instrument? instrument = null, int versionNumber = 1)
    {
        var isNewInstrument = instrument is null;
        instrument ??= TestData.NewInstrument();
        var draft = TestData.DraftVersion(instrument, versionNumber);
        await fixture.InScopeAsync(
            services =>
            {
                if (isNewInstrument)
                {
                    services.GetRequiredService<IInstrumentRepository>().Add(instrument);
                }

                services.GetRequiredService<IInstrumentVersionRepository>().Add(draft);
                return Task.CompletedTask;
            },
            cancellationToken);
        return draft;
    }

    public static async Task<RuleSet> ApprovedRuleSetAsync(
        this IScenarioDatabase fixture, RuleSetKind kind, InstrumentVersion version, CancellationToken cancellationToken)
    {
        var rules = TestData.Ok(RuleSet.Propose(
            kind, TestData.UniqueCode("test_rules"), 1, version, """{"synthetic": true}""", TestData.Now));
        TestData.Ok(rules.Approve(TestData.TestApproval(), version, TestData.Now));
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IRuleSetRepository>().Add(rules);
                return Task.CompletedTask;
            },
            cancellationToken);
        return rules;
    }

    /// <summary>
    /// A new family-voices instrument with an approved synthetic version (sections A and B) and one approved eligibility
    /// rule set per (section, rule JSON) pair, each with a valid definition. Only test databases hold approved rules.
    /// </summary>
    public static async Task<(Instrument Instrument, InstrumentVersion Version, IReadOnlyList<RuleSet> Rules)> EligibilityScenarioAsync(
        this IScenarioDatabase fixture, CancellationToken cancellationToken, params (string Section, string Rule)[] rules)
    {
        var version = await fixture.ApprovedVersionAsync(cancellationToken);
        var instrument = await fixture.InstrumentOfAsync(version, cancellationToken);
        var approved = rules.Select(rule =>
        {
            var ruleSet = TestData.Ok(RuleSet.Propose(
                RuleSetKind.Eligibility,
                TestData.UniqueCode("eligibility"),
                1,
                version,
                $$"""{"schema":"eligibility.v1","target":{"sectionCode":"{{rule.Section}}"},"rule":{{rule.Rule}}}""",
                TestData.Now));
            TestData.Ok(ruleSet.Approve(TestData.TestApproval(), version, TestData.Now));
            return ruleSet;
        }).ToList();
        await fixture.InScopeAsync(
            services =>
            {
                var repository = services.GetRequiredService<IRuleSetRepository>();
                approved.ForEach(repository.Add);
                return Task.CompletedTask;
            },
            cancellationToken);
        return (instrument, version, approved);
    }

    public static async Task<Wave> OpenWaveAsync(this IScenarioDatabase fixture, CancellationToken cancellationToken)
    {
        var wave = TestData.Ok(Wave.Plan(
            TestData.UniqueCode("test_wave"), TestData.Now.AddDays(-1), TestData.Now.AddDays(90), TestData.Now));
        TestData.Ok(wave.Open(TestData.Now));
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IWaveRepository>().Add(wave);
                return Task.CompletedTask;
            },
            cancellationToken);
        return wave;
    }

    /// <summary>Assigns <paramref name="version"/> (or one section of it) to an adult member through a new approved eligibility rule set.</summary>
    public static async Task<AssessmentAssignment> AssignmentAsync(
        this IScenarioDatabase fixture,
        FamilyMember member,
        InstrumentVersion version,
        CancellationToken cancellationToken,
        InstrumentSectionId? sectionId = null,
        Wave? wave = null)
    {
        wave ??= await fixture.OpenWaveAsync(cancellationToken);
        var rules = await fixture.ApprovedRuleSetAsync(RuleSetKind.Eligibility, version, cancellationToken);
        var assignment = TestData.Ok(AssessmentAssignment.Assign(
            member, version, sectionId, wave, rules, AssignmentReason.EligibilityRule, null, TestData.Now));
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IAssessmentAssignmentRepository>().Add(assignment);
                return Task.CompletedTask;
            },
            cancellationToken);
        return assignment;
    }

    /// <summary>A new household, approved version and whole-version assignment, with a started session saved.</summary>
    public static async Task<(AssessmentSession Session, InstrumentVersion Version, AssessmentAssignment Assignment, FamilyMember Member)>
        StartedSessionAsync(this IScenarioDatabase fixture, CancellationToken cancellationToken, string? sectionCode = null)
    {
        var member = (await fixture.SavedHouseholdAsync(cancellationToken)).Members[0];
        var version = await fixture.ApprovedVersionAsync(cancellationToken);
        var sectionId = sectionCode is null ? (InstrumentSectionId?)null : version.Sections.Single(section => section.Code == sectionCode).Id;
        var assignment = await fixture.AssignmentAsync(member, version, cancellationToken, sectionId);
        var session = await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IAssessmentAssignmentRepository>().GetAsync(assignment.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The assignment was not saved.");
                var started = TestData.Ok(AssessmentSession.Start(tracked, TestData.Now));
                services.GetRequiredService<IAssessmentSessionRepository>().Add(started);
                return started;
            },
            cancellationToken);
        return (session, version, assignment, member);
    }

    /// <summary>
    /// A started session where every item was answered with its first option (one commit), then submitted (a second
    /// commit). Answering and submitting are always separate commits: a database trigger refuses answers once the
    /// session row says Submitted, and EF Core does not promise statement order inside one save.
    /// </summary>
    public static async Task<(AssessmentSession Session, InstrumentVersion Version, AssessmentAssignment Assignment, FamilyMember Member)>
        SubmittedSessionAsync(this IScenarioDatabase fixture, CancellationToken cancellationToken, string? sectionCode = null)
    {
        var (started, version, assignment, member) = await fixture.StartedSessionAsync(cancellationToken, sectionCode);
        await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(started.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The session was not saved.");
                foreach (var item in version.GetItems().Where(item => sectionCode is null || version.FindSection(item.InstrumentSectionId)?.Code == sectionCode))
                {
                    TestData.Ok(tracked.Answer(version, item.Id, item.Options.OrderBy(option => option.Value).First().Id, TestData.Now));
                }
            },
            cancellationToken);
        var session = await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(started.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The session was not saved.");
                var trackedAssignment = await services.GetRequiredService<IAssessmentAssignmentRepository>().GetAsync(assignment.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The assignment was not saved.");
                TestData.Ok(tracked.Submit(version, trackedAssignment, TestData.Now));
                return tracked;
            },
            cancellationToken);
        return (session, version, assignment, member);
    }

    /// <summary>
    /// A submitted session scored with a synthetic computation under a new approved scoring rule set, committed with the
    /// session's move to Scored. Only test databases hold such results.
    /// </summary>
    public static async Task<(ScoreResult Score, AssessmentSession Session, RuleSet Rules)> ScoredSessionAsync(
        this IScenarioDatabase fixture, CancellationToken cancellationToken)
    {
        var (submitted, version, _, _) = await fixture.SubmittedSessionAsync(cancellationToken);
        var rules = await fixture.ApprovedRuleSetAsync(RuleSetKind.Scoring, version, cancellationToken);
        var (score, _) = await fixture.RecordSyntheticScoreAsync(submitted.Id, rules, cancellationToken);
        return (score, submitted, rules);
    }

    /// <summary>
    /// A scored session, an approved referral rule set of its version and the member's active referral consent. Only
    /// test databases hold approved referral rules or consent policies.
    /// </summary>
    public static async Task<ReferralScenario> ReferralScenarioAsync(this IScenarioDatabase fixture, CancellationToken cancellationToken)
    {
        var (submitted, version, _, member) = await fixture.SubmittedSessionAsync(cancellationToken);
        var scoringRules = await fixture.ApprovedRuleSetAsync(RuleSetKind.Scoring, version, cancellationToken);
        var (_, scored) = await fixture.RecordSyntheticScoreAsync(submitted.Id, scoringRules, cancellationToken);
        var referralRules = await fixture.ApprovedRuleSetAsync(RuleSetKind.Referral, version, cancellationToken);
        var consent = await fixture.GrantedConsentAsync(
            member, await fixture.ApprovedPolicyAsync(ConsentType.Referral, cancellationToken), cancellationToken);
        return new ReferralScenario(scored, version, member, referralRules, consent);
    }

    /// <summary>A saved open referral in <paramref name="categoryCode"/> for a new <see cref="ReferralScenarioAsync"/>.</summary>
    public static async Task<(SupportReferral Referral, ReferralScenario Scenario)> SavedReferralAsync(
        this IScenarioDatabase fixture, CancellationToken cancellationToken, string categoryCode = "synthetic_category")
    {
        var scenario = await fixture.ReferralScenarioAsync(cancellationToken);
        var referral = TestData.Ok(SupportReferral.Create(
            scenario.Session, scenario.ReferralRules, scenario.Consent, categoryCode, TestData.Now.AddMinutes(2)));
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<ISupportReferralRepository>().Add(referral);
                return Task.CompletedTask;
            },
            cancellationToken);
        return (referral, scenario);
    }

    /// <summary>Scores a submitted session with a synthetic computation and commits it with the session's move to Scored.</summary>
    private static Task<(ScoreResult Score, AssessmentSession Session)> RecordSyntheticScoreAsync(
        this IScenarioDatabase fixture, AssessmentSessionId sessionId, RuleSet rules, CancellationToken cancellationToken) =>
        fixture.InScopeAsync(
            async services =>
            {
                var session = await services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(sessionId, cancellationToken)
                    ?? throw new InvalidOperationException("The session was not saved.");
                var recorded = TestData.Ok(ScoreResult.Record(
                    session, rules, TestData.SyntheticComputation(session), TestData.Now.AddMinutes(1)));
                services.GetRequiredService<IScoreResultRepository>().Add(recorded);
                return (recorded, session);
            },
            cancellationToken);
}
