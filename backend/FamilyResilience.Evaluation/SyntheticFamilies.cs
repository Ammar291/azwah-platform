using FamilyResilience.Application.Assistant;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Evaluation;

/// <summary>
/// Synthetic families for the evaluation. Nothing here is a real person, household or answer; the questions are
/// placeholders, not approved questionnaire content.
/// </summary>
internal static class SyntheticFamilies
{
    private static readonly DateTimeOffset At = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A head of household (41) with a spouse (38) and a son (15), scored 61, band "moderate".</summary>
    public static FamilyContext Head(string language = AssistantLanguages.English) => Build(language, isHead: true);

    /// <summary>The same family, seen by the spouse: answers yes, results no (D-103).</summary>
    public static FamilyContext Spouse(string language = AssistantLanguages.English) => Build(language, isHead: false);

    private static FamilyContext Build(string language, bool isHead)
    {
        var arabic = language == AssistantLanguages.Arabic;
        var caller = isHead ? "member_1" : "member_2";
        return new FamilyContext(
            language,
            new DateOnly(2026, 11, 9),
            new FamilyContextCaller(caller, isHead ? null : HouseholdRelationship.Spouse, isHead, ManagesHousehold: true),
            new FamilyContextHousehold("HH-SYNTH-EVAL", HouseholdStatus.Active, RosterConfirmed: true),
            [
                new FamilyContextMember("member_1", null, 41, EmploymentStatus.Employed, true, true, true, isHead),
                new FamilyContextMember("member_2", HouseholdRelationship.Spouse, 38, EmploymentStatus.Homemaker, false, false, true, !isHead),
                new FamilyContextMember("member_3", HouseholdRelationship.Child, 15, EmploymentStatus.Student, false, false, false, false),
            ],
            [new FamilyContextConsent("member_1", ConsentType.Participation, true, At, null)],
            [
                new FamilyContextQuestionnaire(
                    "member_1", arabic ? "تقييم الأسرة (تجريبي)" : "Household assessment (synthetic)", null, "wave_1",
                    AssignmentStatus.Completed, At, At.AddDays(30), At.AddDays(2)),
            ],
            [
                new FamilyContextSession(
                    "member_1",
                    arabic ? "تقييم الأسرة (تجريبي)" : "Household assessment (synthetic)",
                    null,
                    "wave_1",
                    SessionStatus.Scored,
                    At,
                    At.AddDays(2),
                    arabic
                        ?
                        [
                            new FamilyContextAnswer("كم مرة تقضي أسرتك وقتًا معًا؟", "نادرًا"),
                            new FamilyContextAnswer("ما مدى سهولة إدارة ديون الأسرة؟", "يصعب إدارتها"),
                            new FamilyContextAnswer("ما مدى قدرة أسرتك على مناقشة المشكلات؟", "أحيانًا"),
                        ]
                        :
                        [
                            new FamilyContextAnswer("How often does your family spend time together?", "Rarely"),
                            new FamilyContextAnswer("How manageable are your household debts?", "Hard to manage"),
                            new FamilyContextAnswer("How well does your family talk through problems?", "Sometimes"),
                        ],
                    isHead ? new FamilyContextResult(ScoreStatus.Complete, 61m, "moderate") : null),
            ],
            isHead ? ScoreVisibility.Visible : ScoreVisibility.HeadOfHouseholdOnly,
            [new FamilyContextNextStep("member_1", "financial_counselling", null, At.AddDays(2))]);
    }
}
