using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Questionnaires;

public static class QuestionnaireErrors
{
    public static readonly Error InstrumentCodeInvalid =
        new("questionnaires.instrument_code_invalid", "The instrument code must be lowercase snake case.");

    public static readonly Error InstrumentKindInvalid =
        new("questionnaires.instrument_kind_invalid", "The instrument kind is not recognised.");

    public static readonly Error InstrumentCodeTaken =
        new("questionnaires.instrument_code_taken", "Another instrument already has this code.");

    public static readonly Error InstrumentNotActive =
        new("questionnaires.instrument_not_active", "The instrument is retired.");

    public static readonly Error InstrumentAlreadyRetired =
        new("questionnaires.instrument_already_retired", "The instrument is already retired.");

    public static readonly Error TextTooLong =
        new("questionnaires.text_too_long", "The text is longer than allowed.");

    public static readonly Error VersionNumberInvalid =
        new("questionnaires.version_number_invalid", "The version number must be 1 or more.");

    public static readonly Error VersionNumberTaken =
        new("questionnaires.version_number_taken", "The instrument already has a version with this number.");

    public static readonly Error VersionNotDraft =
        new("questionnaires.version_not_draft", "Only a draft version can change.");

    public static readonly Error VersionNotApproved =
        new("questionnaires.version_not_approved", "The instrument version is not approved.");

    public static readonly Error ContentReferenceInvalid =
        new("questionnaires.content_reference_invalid", "Both content references are required and at most 300 characters.");

    public static readonly Error ContentReferencesMissing =
        new("questionnaires.content_references_missing", "Record where the Arabic and English content came from before approval.");

    public static readonly Error VersionHasNoSections =
        new("questionnaires.version_has_no_sections", "A version needs at least one section before approval.");

    public static readonly Error SectionHasNoItems =
        new("questionnaires.section_has_no_items", "Every section needs at least one item before approval.");

    public static readonly Error ItemNeedsTwoOptions =
        new("questionnaires.item_needs_two_options", "Every single-choice item needs at least two answer options besides any non-substantive one before approval.");

    public static readonly Error EffectiveWindowInvalid =
        new("questionnaires.effective_window_invalid", "The effective window must end after it starts.");

    public static readonly Error EffectiveToAlreadySet =
        new("questionnaires.effective_to_already_set", "The version already has a different end of its effective window.");

    public static readonly Error RetireInFuture =
        new(
            "questionnaires.retire_in_future",
            "A version can only be retired from now or an earlier time; schedule a future end with ScheduleEnd.");

    public static readonly Error EffectiveWindowOverlaps =
        new("questionnaires.effective_window_overlaps", "Another approved version of this instrument is effective in that window.");

    public static readonly Error CodeInvalid =
        new("questionnaires.code_invalid", "Section, item and option codes are letters, digits, '.', '_' or '-', at most 32 characters.");

    public static readonly Error SectionCodeTaken =
        new("questionnaires.section_code_taken", "The version already has a section with this code.");

    public static readonly Error ItemCodeTaken =
        new("questionnaires.item_code_taken", "The version already has an item with this code.");

    public static readonly Error OptionCodeTaken =
        new("questionnaires.option_code_taken", "The item already has an option with this code.");

    public static readonly Error DisplayOrderInvalid =
        new("questionnaires.display_order_invalid", "The display order must be between 1 and 32767.");

    public static readonly Error DisplayOrderTaken =
        new("questionnaires.display_order_taken", "Another entry already uses this display order.");

    public static readonly Error SectionNotFound =
        new("questionnaires.section_not_found", "The section is not part of this version.");

    public static readonly Error ItemNotFound =
        new("questionnaires.item_not_found", "The item is not part of this version.");

    public static readonly Error ItemTypeInvalid =
        new("questionnaires.item_type_invalid", "The item type is not recognised.");

    public static readonly Error ScoringDirectionInvalid =
        new("questionnaires.scoring_direction_invalid", "The scoring direction is not recognised.");

    public static readonly Error ScoringCategoryInvalid =
        new("questionnaires.scoring_category_invalid", "The scoring category must be lowercase snake case.");

    public static readonly Error OptionValueInvalid =
        new("questionnaires.option_value_invalid", "The option value must fit in a small integer.");

    public static readonly Error OptionValueTaken =
        new("questionnaires.option_value_taken", "The item already has an option with this value.");

    public static readonly Error RuleSetKindInvalid =
        new("questionnaires.rule_set_kind_invalid", "The rule-set kind is not recognised.");

    public static readonly Error RuleSetCodeInvalid =
        new("questionnaires.rule_set_code_invalid", "The rule-set code must be lowercase snake case.");

    public static readonly Error RuleSetDefinitionInvalid =
        new("questionnaires.rule_set_definition_invalid", "The rule-set definition must be a JSON object of at most 100,000 characters.");

    public static readonly Error RuleSetNotProposed =
        new("questionnaires.rule_set_not_proposed", "Only a proposed rule set can be approved.");

    public static readonly Error RuleSetNotApproved =
        new("questionnaires.rule_set_not_approved", "The rule set is not approved.");

    public static readonly Error RuleSetVersionMismatch =
        new("questionnaires.rule_set_version_mismatch", "The rule set belongs to another instrument version.");

    public static readonly Error RuleSetVersionExists =
        new("questionnaires.rule_set_version_exists", "A rule set with this kind, code and version already exists.");

    public static readonly Error ArabicTextMissing =
        new("questionnaires.arabic_text_missing", "Every text of the version and its instrument needs Arabic before approval.");

    public static readonly Error InstrumentMismatch =
        new("questionnaires.instrument_mismatch", "The instrument is not the one this version belongs to.");

    public static readonly Error NonSubstantiveOptionTaken =
        new("questionnaires.non_substantive_option_taken", "The item already has a non-substantive option.");

    public static readonly Error ReverseItemScaleInvalid =
        new("questionnaires.reverse_item_scale_invalid", "A reverse-scored item needs consecutive whole-number answer values.");

    public static readonly Error NotScoredItemHasCategory =
        new("questionnaires.not_scored_item_has_category", "An item that is not scored cannot have a scoring category.");

    public static readonly Error RuleSetTargetInvalid =
        new(
            "questionnaires.rule_set_target_invalid",
            "A family-voices rule set targets one module (section code); a household-assessment rule set targets the whole version.");

    public static readonly Error InstrumentNotFound =
        new("questionnaires.instrument_not_found", "The instrument does not exist.");

    public static readonly Error VersionNotFound =
        new("questionnaires.version_not_found", "The instrument version does not exist.");

    public static readonly Error RuleSetNotFound =
        new("questionnaires.rule_set_not_found", "The rule set does not exist.");

    public static readonly Error RuleSetKindNotSupported =
        new("questionnaires.rule_set_kind_not_supported", "Rule sets of this kind have no approved definition schema yet.");
}
