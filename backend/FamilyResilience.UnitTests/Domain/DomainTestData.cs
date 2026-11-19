using System.Collections.Concurrent;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.UnitTests.Domain;

/// <summary>Synthetic domain objects for unit tests. Nothing here resembles a real participant.</summary>
internal static class DomainTestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    public static DateOnly Today => DateOnly.FromDateTime(Now.UtcDateTime);

    public static void Ok(Result result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Test data failed: {result.Error!.Code}");
        }
    }

    public static T Ok<T>(Result<T> result)
    {
        Ok((Result)result);
        return result.Value;
    }

    /// <summary>Born in the current month <paramref name="years"/> years ago, so exactly that age today.</summary>
    public static YearMonth BornYearsAgo(int years) => Ok(YearMonth.Create(Today.Year - years, Today.Month, Today));

    public static FamilyMemberDetails Adult(
        int age = 40,
        bool head = false,
        bool earner = false,
        HouseholdRelationship? relationship = null,
        EmploymentStatus? employment = EmploymentStatus.Employed) =>
        new(BornYearsAgo(age), relationship, employment, head, earner);

    public static FamilyMemberDetails Head(int age = 42) => Adult(age, head: true, earner: true);

    public static FamilyMemberDetails Child(int age) =>
        new(BornYearsAgo(age), HouseholdRelationship.Child, null, false, false);

    public static Household NewHousehold(params FamilyMemberDetails[] roster) =>
        Ok(Household.Create(HouseholdReference.Generate(), roster.Length == 0 ? [Head()] : roster, Now));

    public static Approval TestApproval() => Ok(Approval.Create(
        "approver-subject", Now, "Synthetic test approval"));

    public static ConsentPolicyVersion ApprovedPolicy(ConsentType type)
    {
        var policy = Ok(ConsentPolicyVersion.CreateDraft(type, 1, "consent/test/ar", "consent/test/en", Now));
        Ok(policy.Approve(TestApproval(), Now));
        return policy;
    }

    public static LocalizedText Text(string arabic, string english) => Ok(LocalizedText.Create(arabic, english, 2000));

    private static readonly ConcurrentDictionary<InstrumentId, Instrument> Instruments = new();

    public static LocalizedText DraftText(string english) => Ok(LocalizedText.CreateDraft(null, english, 2000));

    public static Instrument NewInstrument(string code = "test_instrument", InstrumentKind kind = InstrumentKind.FamilyVoices)
    {
        var instrument = Ok(Instrument.Create(code, kind, Text("أداة", "Instrument"), Text("وصف", "Description"), Now));
        Instruments[instrument.Id] = instrument;
        return instrument;
    }

    /// <summary>The instrument a test version belongs to; every instrument made by <see cref="NewInstrument"/> is remembered.</summary>
    public static Instrument InstrumentOf(InstrumentVersion version) => Instruments[version.InstrumentId];

    public static Result ApproveVersion(InstrumentVersion version, DateTimeOffset effectiveFrom, DateTimeOffset? effectiveTo) =>
        version.Approve(InstrumentOf(version), TestApproval(), effectiveFrom, effectiveTo, Now);

    /// <summary>Sections A and B, each with two required single-choice items (A1, A2, B1, B2) and options valued 1–3.</summary>
    public static InstrumentVersion DraftVersion(Instrument? instrument = null, int versionNumber = 1)
    {
        var version = Ok(InstrumentVersion.CreateDraft(instrument ?? NewInstrument(), versionNumber, Now));
        Ok(version.SetContentReferences("test-source (ar)", "test-source (en)", Now));
        foreach (var (sectionCode, sectionOrder) in new[] { ("A", 1), ("B", 2) })
        {
            var section = Ok(version.AddSection(sectionCode, Text("قسم", "Section"), sectionOrder, Now));
            for (var itemOrder = 1; itemOrder <= 2; itemOrder++)
            {
                var item = Ok(version.AddItem(
                    section.Id, $"{sectionCode}{itemOrder}", itemOrder, ItemType.SingleChoice, true, null,
                    ScoringDirection.Forward, Text("سؤال", "Question"), Now));
                for (var value = 1; value <= 3; value++)
                {
                    Ok(version.AddOption(item.Id, $"o{value}", value, value, Text("خيار", "Option"), Now));
                }
            }
        }

        return version;
    }

    public static InstrumentVersion ApprovedVersion(Instrument? instrument = null)
    {
        var version = DraftVersion(instrument);
        Ok(ApproveVersion(version, Now.AddDays(-1), null));
        return version;
    }

    public static RuleSet ApprovedRuleSet(RuleSetKind kind, InstrumentVersion version, string code = "test_rules")
    {
        var rules = Ok(RuleSet.Propose(kind, code, 1, version, """{"synthetic": true}""", Now));
        Ok(rules.Approve(TestApproval(), version, Now));
        return rules;
    }

    public static Wave OpenWave(string code = "test_wave")
    {
        var wave = Ok(Wave.Plan(code, Now.AddDays(-1), Now.AddDays(30), Now));
        Ok(wave.Open(Now));
        return wave;
    }

    public static AssessmentAssignment Assign(
        FamilyMember member, InstrumentVersion version, InstrumentSectionId? sectionId = null, Wave? wave = null) =>
        Ok(AssessmentAssignment.Assign(
            member, version, sectionId, wave ?? OpenWave(), ApprovedRuleSet(RuleSetKind.Eligibility, version),
            AssignmentReason.EligibilityRule, null, Now));

    /// <summary>A whole-version session where every item was answered with its first option, then submitted.</summary>
    public static (AssessmentSession Session, InstrumentVersion Version, AssessmentAssignment Assignment) SubmittedSession()
    {
        var version = ApprovedVersion();
        var assignment = Assign(NewHousehold().Members[0], version);
        var session = Ok(AssessmentSession.Start(assignment, Now));
        foreach (var item in version.GetItems())
        {
            Ok(session.Answer(version, item.Id, item.Options[0].Id, Now));
        }

        Ok(session.Submit(version, assignment, Now));
        return (session, version, assignment);
    }

    /// <summary>A synthetic computation: every answer counts as its raw value. Not a real scoring rule.</summary>
    public static ScoringComputation SyntheticComputation(AssessmentSession session, string? tierCode = null) =>
        new(
            "synthetic_policy",
            1,
            ScoreStatus.Complete,
            null,
            session.Responses.Sum(response => response.RawValue),
            tierCode,
            session.Responses.Select(response => new ItemContribution(response.InstrumentItemId, response.RawValue, response.RawValue, null)).ToList(),
            []);

    public static (AssessmentSession Session, InstrumentVersion Version, ScoreResult Score) ScoredSession()
    {
        var (session, version, _) = SubmittedSession();
        var score = Ok(ScoreResult.Record(
            session, ApprovedRuleSet(RuleSetKind.Scoring, version), SyntheticComputation(session, "test_tier"), Now));
        return (session, version, score);
    }

    public static ConsentRecord ReferralConsentFor(FamilyMember member) =>
        Ok(ConsentRecord.Grant(member, ApprovedPolicy(ConsentType.Referral), ConsentCaptureMethod.InApp, null, Now));
}
