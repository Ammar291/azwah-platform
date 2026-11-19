using FamilyResilience.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace FamilyResilience.Api.Errors;

/// <summary>
/// Turns an expected failure into ProblemDetails. Every error code is classified once: 400 when the request itself is
/// wrong, 403 when the caller may see the resource but a required consent is missing, 404 when the resource does not
/// exist (or the caller may not know it does), 409 when the request conflicts with the current state, 429 when a rate
/// limit refused it, 502 when a model behind the API failed, 503 when a feature has no model configured, and 500 for
/// codes that can only mean a server bug or fault (a fault caught only so its problem can carry the crisis notice, D-138).
/// ErrorResultsTests proves every declared error is classified.
/// </summary>
public static class ErrorResults
{
    private static readonly string[] BadRequest =
    [
        "analytics.export_column_invalid", "analytics.export_dataset_invalid", "analytics.export_filter_invalid",
        "assistant.websocket_required",
        "assignments.expiry_invalid", "assignments.reason_invalid", "assignments.wave_code_invalid", "assignments.wave_window_invalid",
        "common.approval_source_invalid", "common.approver_invalid", "common.arabic_text_not_arabic", "common.month_out_of_range",
        "common.text_required", "common.text_too_long", "common.time_not_utc", "common.year_month_in_future", "common.year_out_of_range",
        "consent.actor_id_too_long", "consent.capture_method_invalid", "consent.consent_type_invalid",
        "consent.document_reference_invalid", "consent.version_number_invalid", "consent.withdrawn_before_granted",
        "eligibility.definition_invalid",
        "households.birth_month_in_future", "households.employment_status_invalid", "households.head_has_relationship",
        "households.head_under_18", "households.primary_earner_under_18", "households.reference_invalid",
        "households.erasure_reference_mismatch", "households.relationship_invalid", "households.roster_empty",
        "questionnaires.code_invalid", "questionnaires.content_reference_invalid", "questionnaires.display_order_invalid",
        "questionnaires.display_order_taken", "questionnaires.effective_window_invalid", "questionnaires.instrument_code_invalid",
        "questionnaires.instrument_kind_invalid", "questionnaires.item_code_taken", "questionnaires.item_not_found",
        "questionnaires.item_type_invalid", "questionnaires.non_substantive_option_taken",
        "questionnaires.not_scored_item_has_category", "questionnaires.option_code_taken", "questionnaires.option_value_invalid",
        "questionnaires.option_value_taken", "questionnaires.rule_set_code_invalid", "questionnaires.rule_set_definition_invalid",
        "questionnaires.rule_set_kind_invalid", "questionnaires.rule_set_kind_not_supported", "questionnaires.rule_set_target_invalid",
        "questionnaires.scoring_category_invalid", "questionnaires.scoring_direction_invalid", "questionnaires.section_code_taken",
        "questionnaires.section_not_found", "questionnaires.text_too_long", "questionnaires.version_number_invalid",
        "privacy.resolution_invalid", "privacy.type_invalid",
        "requests.https_required",
        "referrals.actor_id_too_long", "referrals.category_code_invalid", "referrals.definition_invalid",
        "referrals.partner_categories_invalid", "referrals.partner_code_invalid",
        "referrals.resolution_code_invalid", "referrals.service_code_invalid",
        "scoring.definition_invalid", "scoring.item_direction_unspecified", "scoring.item_scale_invalid", "scoring.no_scored_items",
        "scoring.optional_item_scored",
        "scoring.tier_bands_invalid",
        "sessions.item_not_in_section", "sessions.item_not_in_version", "sessions.option_not_in_item",
        "support_resources.audiences_invalid", "support_resources.categories_invalid", "support_resources.code_invalid",
        "support_resources.review_due_invalid", "support_resources.service_languages_invalid", "support_resources.url_invalid",
        "support_resources.visibility_invalid",
    ];

    private static readonly string[] Forbidden =
    [
        "assistant.score_report_head_only",
        "sessions.participation_consent_required",
    ];

    private static readonly string[] NotFound =
    [
        "analytics.export_not_found",
        "assignments.assignment_not_found", "assignments.wave_not_found", "consent.policy_not_found", "demo_tools.disabled",
        "households.household_not_found",
        "households.member_not_found", "privacy.request_not_found", "questionnaires.instrument_not_found", "questionnaires.rule_set_not_found",
        "questionnaires.version_not_found", "referrals.partner_not_found", "referrals.referral_not_found",
        "safeguarding.alert_not_found", "sessions.session_not_found",
        "support_resources.not_found",
    ];

    private static readonly string[] Conflict =
    [
        "analytics.export_attempts_exhausted", "analytics.export_daily_limit_reached", "analytics.export_expired",
        "analytics.export_limit_reached", "analytics.export_not_ready",
        "analytics.export_stalled", "analytics.export_storage_not_configured", "analytics.export_too_large",
        "assistant.score_report_not_ready",
        "assignments.already_assigned", "assignments.already_finished", "assignments.assignment_expired",
        "assignments.member_not_active", "assignments.member_under_18", "assignments.not_assigned", "assignments.not_in_progress",
        "assignments.rule_set_not_approved", "assignments.rule_set_version_mismatch", "assignments.rule_set_wrong_kind",
        "assignments.section_not_in_version", "assignments.version_not_effective", "assignments.wave_already_closed",
        "assignments.wave_ambiguous", "assignments.wave_closed", "assignments.wave_code_taken", "assignments.wave_not_planned",
        "consent.already_active", "consent.member_not_active", "consent.member_under_18", "consent.not_active",
        "demo_tools.nothing_to_assign", "demo_tools.roster_not_confirmed",
        "consent.policy_not_approved", "consent.policy_not_current", "consent.policy_not_draft", "consent.policy_version_exists",
        "eligibility.ambiguous_rules",
        "households.account_holder_under_18", "households.already_member", "households.already_withdrawn",
        "households.erasure_needs_withdrawal", "households.erasure_not_configured",
        "households.head_already_exists", "households.manager_under_18", "households.member_already_linked",
        "households.member_has_consent", "households.member_is_manager", "households.not_active",
        "households.primary_earner_already_exists", "households.reference_taken", "households.roster_full", "households.roster_has_no_head",
        "households.roster_missing_employment_status", "households.roster_missing_relationship",
        "persistence.concurrency_conflict", "persistence.conflict", "requests.idempotency_key_reused",
        "privacy.request_already_closed", "privacy.request_already_open",
        "questionnaires.arabic_text_missing", "questionnaires.content_references_missing", "questionnaires.effective_to_already_set",
        "questionnaires.effective_window_overlaps", "questionnaires.instrument_already_retired",
        "questionnaires.instrument_code_taken", "questionnaires.instrument_mismatch", "questionnaires.instrument_not_active",
        "questionnaires.item_needs_two_options", "questionnaires.retire_in_future", "questionnaires.reverse_item_scale_invalid",
        "questionnaires.rule_set_not_approved", "questionnaires.rule_set_not_proposed", "questionnaires.rule_set_version_exists",
        "questionnaires.rule_set_version_mismatch", "questionnaires.section_has_no_items", "questionnaires.version_has_no_sections",
        "questionnaires.version_not_approved", "questionnaires.version_not_draft", "questionnaires.version_number_taken",
        "referrals.already_closed", "referrals.already_referred", "referrals.ambiguous_rules", "referrals.consent_member_mismatch",
        "referrals.consent_not_active", "referrals.consent_wrong_type", "referrals.not_open", "referrals.partner_category_mismatch",
        "referrals.partner_code_taken", "referrals.partner_inactive", "referrals.rule_set_not_approved",
        "referrals.rule_set_version_mismatch", "referrals.rule_set_wrong_kind", "referrals.session_not_scored",
        "scoring.already_scored", "scoring.ambiguous_rules", "scoring.answer_invalid", "scoring.policy_not_approved",
        "scoring.policy_unsupported", "scoring.rule_set_not_approved", "scoring.rule_set_version_mismatch",
        "scoring.rule_set_wrong_kind", "scoring.session_not_submitted", "scoring.unanswered_items",
        "safeguarding.alert_not_open", "safeguarding.alert_resolved",
        "sessions.duplicate_response", "sessions.not_in_progress", "sessions.not_submitted", "sessions.required_items_unanswered",
        "sessions.session_already_exists", "sessions.time_before_last_change", "sessions.version_not_published",
        "support_resources.approved_version_exists", "support_resources.arabic_text_missing", "support_resources.not_approved",
        "support_resources.not_draft", "support_resources.version_number_taken",
    ];

    private static readonly string[] TooManyRequests =
    [
        "requests.rate_limited",
    ];

    private static readonly string[] BadGateway =
    [
        "assistant.model_failed",
    ];

    private static readonly string[] ServiceUnavailable =
    [
        "assistant.unavailable",
    ];

    private static readonly string[] Internal =
    [
        "analytics.export_transition_invalid",
        "assistant.reply_faulted",
        "audit.action_invalid", "audit.actor_id_not_allowed", "audit.actor_id_required", "audit.actor_id_too_long",
        "audit.actor_type_invalid", "audit.correlation_id_too_long", "audit.occurred_at_not_utc", "audit.outcome_invalid",
        "audit.resource_id_too_long", "audit.resource_type_invalid",
        "identity.roles_invalid", "identity.subject_invalid",
        "privacy.actor_invalid",
        "referrals.referral_mismatch", "referrals.score_mismatch",
        "scoring.computation_inconsistent", "scoring.contribution_for_unanswered_item", "scoring.contribution_raw_value_mismatch",
        "scoring.duplicate_contribution", "scoring.score_out_of_range", "scoring.tier_code_invalid",
        "sessions.assignment_mismatch", "sessions.version_mismatch",
        "support_resources.search_faulted", "support_resources.version_number_invalid",
    ];

    private static readonly Dictionary<string, int> StatusByCode = Build();

    public static IReadOnlyDictionary<string, int> StatusCodesByErrorCode => StatusByCode;

    public static int StatusCodeFor(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return StatusByCode.TryGetValue(error.Code, out var status)
            ? status
            : throw new InvalidOperationException($"Error '{error.Code}' has no HTTP status in ErrorResults.");
    }

    /// <summary>ProblemDetails for <paramref name="error"/>, with its code. A 500 carries no detail and no code.</summary>
    public static ObjectResult ToProblem(this ControllerBase controller, Error error)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var status = StatusCodeFor(error);
        var problem = status == StatusCodes.Status500InternalServerError
            ? controller.ProblemDetailsFactory.CreateProblemDetails(
                controller.HttpContext, status, title: "An unexpected error occurred.")
            : controller.ProblemDetailsFactory.CreateProblemDetails(controller.HttpContext, status, detail: error.Message);
        if (status != StatusCodes.Status500InternalServerError)
        {
            problem.Extensions["code"] = error.Code;
        }

        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }

    private static Dictionary<string, int> Build()
    {
        // Dictionary.Add throws on a code listed twice, so a code can never sit in two sets.
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (codes, status) in new[]
                 {
                     (BadRequest, StatusCodes.Status400BadRequest),
                     (Forbidden, StatusCodes.Status403Forbidden),
                     (NotFound, StatusCodes.Status404NotFound),
                     (Conflict, StatusCodes.Status409Conflict),
                     (TooManyRequests, StatusCodes.Status429TooManyRequests),
                     (BadGateway, StatusCodes.Status502BadGateway),
                     (ServiceUnavailable, StatusCodes.Status503ServiceUnavailable),
                     (Internal, StatusCodes.Status500InternalServerError),
                 })
        {
            foreach (var code in codes)
            {
                map.Add(code, status);
            }
        }

        return map;
    }
}
