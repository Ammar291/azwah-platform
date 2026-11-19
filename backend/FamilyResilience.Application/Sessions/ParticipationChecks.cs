using FamilyResilience.Application.Consent;
using FamilyResilience.Application.Households;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Sessions;

internal static class ParticipationChecks
{
    /// <summary>
    /// Answers are collected only from a member of an active household (open question 25: handlers check until the
    /// domain decides) who has active participation consent. Starting, answering and submitting all ask.
    /// </summary>
    public static async Task<Result> EnsureMayAnswerAsync(
        FamilyMemberId memberId, IHouseholdRepository households, IConsentRecordRepository consents, CancellationToken cancellationToken)
    {
        if (await households.StatusOfMemberHouseholdAsync(memberId, cancellationToken) != HouseholdStatus.Active)
        {
            return Result.Failure(HouseholdErrors.NotActive);
        }

        return await consents.GetActiveAsync(memberId, ConsentType.Participation, cancellationToken) is null
            ? Result.Failure(SessionErrors.ParticipationConsentRequired)
            : Result.Success();
    }
}
