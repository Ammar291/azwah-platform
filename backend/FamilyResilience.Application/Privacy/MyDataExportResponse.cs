using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Consent;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Referrals;

namespace FamilyResilience.Application.Privacy;

/// <summary>
/// A copy of what the platform holds about the members the caller may act for (D-89). <c>notIncluded</c> names what is
/// held but left out: scores, tiers and support recommendations are not shown to participants until that display is
/// approved (D-72).
/// </summary>
public sealed record MyDataExportResponse(
    DateTimeOffset GeneratedAt,
    string Subject,
    MyHouseholdResponse? Household,
    IReadOnlyList<MemberResponse> Members,
    IReadOnlyList<ConsentRecordResponse> Consents,
    IReadOnlyList<ParticipantAssignmentResponse> Assignments,
    IReadOnlyList<MyDataSessionResponse> Sessions,
    IReadOnlyList<ReferralResponse> Referrals,
    IReadOnlyList<PrivacyRequestResponse> PrivacyRequests,
    IReadOnlyList<string> NotIncluded);
