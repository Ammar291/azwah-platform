using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Households;

public static class HouseholdErrors
{
    public static readonly Error ErasureNotConfigured =
        new("households.erasure_not_configured", "Erasure is not set up: Erasure:ConsentRecords must say what happens to consent records.");

    public static readonly Error ErasureReferenceMismatch =
        new("households.erasure_reference_mismatch", "The reference given is not this household's.");

    public static readonly Error ErasureNeedsWithdrawal =
        new("households.erasure_needs_withdrawal", "Only a household that has withdrawn can be erased.");

    public static readonly Error ReferenceInvalid =
        new("households.reference_invalid", "A household reference looks like HH-XXXX-XXXX.");

    public static readonly Error ReferenceTaken =
        new("households.reference_taken", "Another household already has this reference.");

    public static readonly Error RosterEmpty =
        new("households.roster_empty", "A household needs at least one member.");

    public static readonly Error RosterFull =
        new("households.roster_full", "A household can have at most 30 active members.");

    public static readonly Error NotActive =
        new("households.not_active", "The household has withdrawn; its roster cannot change.");

    public static readonly Error AlreadyWithdrawn =
        new("households.already_withdrawn", "The household has already withdrawn.");

    public static readonly Error MemberNotFound =
        new("households.member_not_found", "The member is not an active member of this household.");

    public static readonly Error HeadAlreadyExists =
        new("households.head_already_exists", "The household already has an active head.");

    public static readonly Error PrimaryEarnerAlreadyExists =
        new("households.primary_earner_already_exists", "The household already has an active primary income earner.");

    public static readonly Error HeadUnder18 =
        new("households.head_under_18", "The head of household must be 18 or over.");

    public static readonly Error PrimaryEarnerUnder18 =
        new("households.primary_earner_under_18", "The primary income earner must be 18 or over.");

    public static readonly Error HeadHasRelationship =
        new("households.head_has_relationship", "The head has no relationship to the head.");

    public static readonly Error RelationshipInvalid =
        new("households.relationship_invalid", "The relationship is not recognised.");

    public static readonly Error EmploymentStatusInvalid =
        new("households.employment_status_invalid", "The employment status is not recognised.");

    public static readonly Error BirthMonthInFuture =
        new("households.birth_month_in_future", "The birth month must not be in the future.");

    public static readonly Error HouseholdNotFound =
        new("households.household_not_found", "The household does not exist.");

    public static readonly Error AlreadyMember =
        new("households.already_member", "The caller already belongs to a household.");

    public static readonly Error ManagerUnder18 =
        new("households.manager_under_18", "The person managing the household must be 18 or over.");

    public static readonly Error MemberAlreadyLinked =
        new("households.member_already_linked", "The member already has their own account.");

    public static readonly Error AccountHolderUnder18 =
        new("households.account_holder_under_18", "Only members aged 18 or over can have their own account.");

    public static readonly Error MemberHasConsent =
        new("households.member_has_consent", "Withdraw the member's consents before recording them as under 18.");

    public static readonly Error MemberIsManager =
        new("households.member_is_manager", "The household's manager cannot be removed from the roster.");

    public static readonly Error RosterHasNoHead =
        new("households.roster_has_no_head", "A confirmed roster needs exactly one active head of household.");

    public static readonly Error RosterMissingRelationship =
        new("households.roster_missing_relationship", "Every active member other than the head needs a relationship.");

    public static readonly Error RosterMissingEmploymentStatus =
        new("households.roster_missing_employment_status", "Every active adult member needs an employment status.");
}
