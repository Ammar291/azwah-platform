using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Scoring;

public static class ScoringErrors
{
    public static readonly Error SessionNotSubmitted =
        new("scoring.session_not_submitted", "Only a submitted session can be scored.");

    public static readonly Error RuleSetWrongKind =
        new("scoring.rule_set_wrong_kind", "Scores come only from a scoring rule set.");

    public static readonly Error RuleSetNotApproved =
        new("scoring.rule_set_not_approved", "The scoring rule set is not approved.");

    public static readonly Error RuleSetVersionMismatch =
        new("scoring.rule_set_version_mismatch", "The scoring rule set belongs to another instrument version.");

    public static readonly Error TierCodeInvalid =
        new("scoring.tier_code_invalid", "The tier code must be lowercase snake case.");

    public static readonly Error ScoreOutOfRange =
        new("scoring.score_out_of_range", "Scores have at most 6 integer digits and 3 decimals.");

    public static readonly Error DuplicateContribution =
        new("scoring.duplicate_contribution", "An item can contribute to a score only once.");

    public static readonly Error ContributionForUnansweredItem =
        new("scoring.contribution_for_unanswered_item", "A contribution names an item the session did not answer.");

    public static readonly Error ContributionRawValueMismatch =
        new("scoring.contribution_raw_value_mismatch", "A contribution's raw value differs from the recorded answer.");

    public static readonly Error AlreadyScored =
        new("scoring.already_scored", "The session already has a score from this rule set.");

    public static readonly Error PolicyNotApproved =
        new("scoring.policy_not_approved", "No approved scoring rule set covers this session; scoring stays disabled until one is approved.");

    public static readonly Error PolicyUnsupported =
        new("scoring.policy_unsupported", "The scoring policy or algorithm version is not supported by this engine.");

    public static readonly Error DefinitionInvalid =
        new("scoring.definition_invalid", "The scoring definition is invalid.");

    public static readonly Error ItemDirectionUnspecified =
        new("scoring.item_direction_unspecified", "An item in scope has no approved scoring direction.");

    public static readonly Error ItemScaleInvalid =
        new("scoring.item_scale_invalid", "A scored item's answer values are not consecutive whole numbers.");

    public static readonly Error NoScoredItems =
        new("scoring.no_scored_items", "No item in scope is scored.");

    public static readonly Error TierBandsInvalid =
        new("scoring.tier_bands_invalid", "Tier bands must be contiguous whole-number ranges that exactly cover the possible totals.");

    public static readonly Error OptionalItemScored =
        new("scoring.optional_item_scored", "Unanswered items are rejected, so every scored item in scope must be required.");

    public static readonly Error UnansweredItems =
        new("scoring.unanswered_items", "Items in scope are unanswered and the approved policy rejects unanswered items.");

    public static readonly Error AnswerInvalid =
        new("scoring.answer_invalid", "An answer does not match an option of its item in the scored scope.");

    public static readonly Error AmbiguousRules =
        new("scoring.ambiguous_rules", "More than one approved scoring rule set covers this session.");

    public static readonly Error ComputationInconsistent =
        new("scoring.computation_inconsistent", "The computed score is inconsistent with its status or the session.");
}
