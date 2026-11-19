using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Referrals;

public static class ReferralErrors
{
    public static readonly Error SessionNotScored =
        new("referrals.session_not_scored", "Referrals follow from a scored session.");

    public static readonly Error RuleSetWrongKind =
        new("referrals.rule_set_wrong_kind", "Referrals come only from a referral rule set.");

    public static readonly Error RuleSetNotApproved =
        new("referrals.rule_set_not_approved", "The referral rule set is not approved.");

    public static readonly Error RuleSetVersionMismatch =
        new("referrals.rule_set_version_mismatch", "The referral rule set belongs to another instrument version.");

    public static readonly Error ConsentNotActive =
        new("referrals.consent_not_active", "The referral consent has been withdrawn.");

    public static readonly Error ConsentWrongType =
        new("referrals.consent_wrong_type", "A referral needs referral consent.");

    public static readonly Error ConsentMemberMismatch =
        new("referrals.consent_member_mismatch", "The consent belongs to another member.");

    public static readonly Error CategoryCodeInvalid =
        new("referrals.category_code_invalid", "The referral category must be lowercase snake case.");

    public static readonly Error ServiceCodeInvalid =
        new("referrals.service_code_invalid", "The service code must be lowercase snake case.");

    public static readonly Error ResolutionCodeInvalid =
        new("referrals.resolution_code_invalid", "The resolution code must be lowercase snake case.");

    public static readonly Error ActorIdTooLong =
        new("referrals.actor_id_too_long", "The actor ID must be at most 200 characters.");

    public static readonly Error NotOpen =
        new("referrals.not_open", "Only an open referral can be assigned to a service.");

    public static readonly Error AlreadyClosed =
        new("referrals.already_closed", "The referral is already resolved or cancelled.");

    public static readonly Error DefinitionInvalid =
        new("referrals.definition_invalid", "The referral definition is invalid.");

    public static readonly Error AmbiguousRules =
        new("referrals.ambiguous_rules", "More than one approved referral rule set covers the same scope.");

    public static readonly Error ScoreMismatch =
        new("referrals.score_mismatch", "The score result belongs to another session.");

    public static readonly Error ReferralMismatch =
        new("referrals.referral_mismatch", "The referral belongs to another session or category.");

    public static readonly Error AlreadyReferred =
        new("referrals.already_referred", "The session already has a live referral in this category.");

    public static readonly Error ReferralNotFound =
        new("referrals.referral_not_found", "The referral does not exist.");

    public static readonly Error PartnerCodeInvalid =
        new("referrals.partner_code_invalid", "The partner code must be lowercase snake case.");

    public static readonly Error PartnerCategoriesInvalid =
        new("referrals.partner_categories_invalid", "A partner covers 1 to 20 distinct snake-case support categories.");

    public static readonly Error PartnerNotFound =
        new("referrals.partner_not_found", "The partner organisation does not exist.");

    public static readonly Error PartnerCodeTaken =
        new("referrals.partner_code_taken", "Another partner organisation already has this code.");

    public static readonly Error PartnerInactive =
        new("referrals.partner_inactive", "The partner organisation is not active.");

    public static readonly Error PartnerCategoryMismatch =
        new("referrals.partner_category_mismatch", "The partner organisation does not cover the referral's support category.");
}
