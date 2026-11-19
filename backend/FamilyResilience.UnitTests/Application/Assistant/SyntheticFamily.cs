using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Application.Consent;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Application.Referrals;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Referrals;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Application.Assistant;

/// <summary>
/// A synthetic household (head 42, spouse 39, child 9) whose head answered one synthetic module, which was scored
/// <c>band_low</c> and led to one recommendation, with participation consent. The caller is the household's manager:
/// the head, or the spouse answering for the head. Nothing here resembles a real participant or real content.
/// </summary>
internal sealed class SyntheticFamily
{
    public const string Subject = "caller-subject";
    public const string BandCode = "band_low";
    public const string CategoryCode = "synthetic_support";

    private SyntheticFamily(bool callerIsHead)
    {
        CallerId = PlatformUserId.New();
        Household = Ok(Household.Enrol(
            HouseholdReference.Generate(),
            [Head(), Adult(39, relationship: HouseholdRelationship.Spouse), Child(9)],
            callerIsHead ? 0 : 1,
            CallerId,
            Now));
        HeadMember = Household.Members[0];

        Version = SyntheticVersion();
        var wave = OpenWave("synthetic_wave");
        Assignment = Assign(HeadMember, Version, null, wave);
        Session = Ok(AssessmentSession.Start(Assignment, Now));
        var items = Version.GetItems().OrderBy(item => item.DisplayOrder).ToList();
        // Answered out of order, so the context must put them back in question order.
        Ok(Session.Answer(Version, items[1].Id, items[1].Options[0].Id, Now));
        Ok(Session.Answer(Version, items[0].Id, items[0].Options[1].Id, Now));
        Ok(Session.Submit(Version, Assignment, Now));
        Score = Ok(ScoreResult.Record(Session, ApprovedRuleSet(RuleSetKind.Scoring, Version), SyntheticComputation(Session, BandCode), Now));
        Recommendation = Ok(SupportRecommendation.Record(
            Session, Score, ApprovedRuleSet(RuleSetKind.Referral, Version, "synthetic_referral"), CategoryCode, null, Now));
        Policy = ApprovedPolicy(ConsentType.Participation);
        Consent = Ok(ConsentRecord.Grant(HeadMember, Policy, ConsentCaptureMethod.InApp, null, Now));
        AssignmentView = new ParticipantAssignmentResponse(
            Assignment.Id.Value,
            HeadMember.Id.Value,
            "synthetic_instrument",
            new TextDto("أداة تجريبية", "Synthetic instrument"),
            Version.VersionNumber,
            null,
            null,
            wave.Id.Value,
            wave.Code,
            Assignment.Status,
            Now,
            Now.AddDays(30),
            Now,
            null,
            InstrumentKind.HouseholdAssessment);
    }

    public PlatformUserId CallerId { get; }

    public Household Household { get; }

    public FamilyMember HeadMember { get; }

    public InstrumentVersion Version { get; }

    public AssessmentAssignment Assignment { get; }

    public AssessmentSession Session { get; }

    public ScoreResult Score { get; }

    public SupportRecommendation Recommendation { get; }

    public ConsentPolicyVersion Policy { get; }

    public ConsentRecord Consent { get; }

    public ParticipantAssignmentResponse AssignmentView { get; }

    public InMemoryScoreResults ScoreResults { get; private set; } = null!;

    public static SyntheticFamily HeadSignsIn() => new(callerIsHead: true);

    public static SyntheticFamily SpouseManagesForTheHead() => new(callerIsHead: false);

    /// <summary>A builder over this family's records, for the family's caller.</summary>
    public FamilyContextBuilder Builder() => BuilderFor(new CallerRecord(CallerId, Household));

    /// <summary>A builder over this family's records, for whoever <paramref name="record"/> says the caller is.</summary>
    public FamilyContextBuilder BuilderFor(CallerRecord? record) => BuilderOver(new FixedCallerQueries(record));

    /// <summary>A builder over this family's records whose caller lookups go to <paramref name="queries"/>.</summary>
    public FamilyContextBuilder BuilderOver(IAccessQueries queries)
    {
        var access = new CallerAccess(new FakeCurrentUser(true, Subject), queries);
        var consents = new InMemoryConsentRecords(Consent);
        var policies = new InMemoryConsentPolicies(Policy);
        ScoreResults = new InMemoryScoreResults(Score);
        return new FamilyContextBuilder(
            access,
            access,
            new ListMyConsents(access, consents, policies),
            new ListMyAssignments(access, new InMemoryAssignmentReadModel(AssignmentView)),
            new ListMyRecommendations(access, new InMemoryRecommendations(Recommendation), new NoReferrals()),
            new InMemorySessions(Session),
            new InMemoryVersions(Version),
            ScoreResults,
            new FixedTimeProvider(Now));
    }

    /// <summary>Section A with two synthetic questions and two synthetic options each, in both languages, approved.</summary>
    private static InstrumentVersion SyntheticVersion()
    {
        var version = Ok(InstrumentVersion.CreateDraft(NewInstrument("synthetic_instrument"), 1, Now));
        Ok(version.SetContentReferences("synthetic-source (ar)", "synthetic-source (en)", Now));
        var section = Ok(version.AddSection("A", Text("قسم تجريبي", "Synthetic section"), 1, Now));
        foreach (var (order, arabic, english) in new[]
                 {
                     (1, "السؤال التجريبي الأول", "Synthetic question one"),
                     (2, "السؤال التجريبي الثاني", "Synthetic question two"),
                 })
        {
            var item = Ok(version.AddItem(
                section.Id, $"A{order}", order, ItemType.SingleChoice, true, null, ScoringDirection.Forward, Text(arabic, english), Now));
            Ok(version.AddOption(item.Id, "o1", 1, 1, Text($"خيار منخفض {order}", $"Synthetic low {order}"), Now));
            Ok(version.AddOption(item.Id, "o2", 2, 2, Text($"خيار مرتفع {order}", $"Synthetic high {order}"), Now));
        }

        Ok(ApproveVersion(version, Now.AddDays(-1), null));
        return version;
    }
}
