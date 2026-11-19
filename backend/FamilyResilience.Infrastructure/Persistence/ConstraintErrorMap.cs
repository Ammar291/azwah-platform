using System.Diagnostics.CodeAnalysis;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Privacy;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Referrals;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.Domain.SupportResources;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FamilyResilience.Infrastructure.Persistence;

/// <summary>
/// Turns expected database conflicts (unique and exclusion violations) into <see cref="Result"/> errors. Every named
/// unique index or exclusion constraint that a normal request can hit is listed here with its module error. Check,
/// foreign-key and trigger violations are not mapped: the domain stops those first, so they mean a bug.
/// </summary>
public static class ConstraintErrorMap
{
    private static readonly Dictionary<string, Error> Errors = new(StringComparer.Ordinal)
    {
        ["ux_consent_policy_versions_type_version"] = ConsentErrors.PolicyVersionExists,
        ["ux_consent_records_one_active_per_type"] = ConsentErrors.AlreadyActive,
        ["ux_households_reference"] = HouseholdErrors.ReferenceTaken,
        ["ux_privacy_requests_one_open_per_type"] = PrivacyErrors.RequestAlreadyOpen,
        // Two enrolments by one subject at once: the one that loses registering the user is already a member.
        ["ux_platform_users_subject"] = HouseholdErrors.AlreadyMember,
        ["ux_household_memberships_one_active_per_user"] = HouseholdErrors.AlreadyMember,
        ["ux_family_members_one_active_head"] = HouseholdErrors.HeadAlreadyExists,
        ["ux_family_members_one_active_primary_earner"] = HouseholdErrors.PrimaryEarnerAlreadyExists,
        ["ux_instruments_code"] = QuestionnaireErrors.InstrumentCodeTaken,
        ["ux_instrument_versions_instrument_version"] = QuestionnaireErrors.VersionNumberTaken,
        ["ux_rule_sets_kind_code_version"] = QuestionnaireErrors.RuleSetVersionExists,
        ["ux_waves_code"] = AssignmentErrors.WaveCodeTaken,
        ["ux_assessment_assignments_live"] = AssignmentErrors.AlreadyAssigned,
        ["ux_assessment_sessions_live"] = SessionErrors.SessionAlreadyExists,
        ["ux_assessment_responses_session_item"] = SessionErrors.DuplicateResponse,
        ["ux_score_results_session_rule_set"] = ScoringErrors.AlreadyScored,
        ["ux_support_referrals_live"] = ReferralErrors.AlreadyReferred,
        ["ux_partner_organisations_code"] = ReferralErrors.PartnerCodeTaken,
        ["ux_support_resources_code_version"] = SupportResourceErrors.VersionNumberTaken,
        ["ux_support_resources_one_approved_per_code"] = SupportResourceErrors.ApprovedVersionExists,
        ["ux_idempotency_keys_subject_key"] = RequestErrors.IdempotencyKeyReused,
        // Two first requests of one subject at once: the other one recorded the roles.
        ["ux_role_snapshots_subject"] = PersistenceErrors.Conflict,
        ["ex_instrument_versions_effective_window"] = QuestionnaireErrors.EffectiveWindowOverlaps,
    };

    /// <summary>The constraint and index names this map recognises; a test proves each one exists in the database.</summary>
    public static IReadOnlyCollection<string> KnownConstraintNames => Errors.Keys;

    public static bool TryMap(DbUpdateException exception, [NotNullWhen(true)] out Error? error)
    {
        error = null;
        if (exception.InnerException is not PostgresException postgres
            || postgres.SqlState is not (PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ExclusionViolation))
        {
            return false;
        }

        error = postgres.ConstraintName is { } name && Errors.TryGetValue(name, out var mapped)
            ? mapped
            : PersistenceErrors.Conflict;
        return true;
    }
}
