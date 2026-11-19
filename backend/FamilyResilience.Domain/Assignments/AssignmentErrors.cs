using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Assignments;

public static class AssignmentErrors
{
    public static readonly Error WaveCodeInvalid =
        new("assignments.wave_code_invalid", "The wave code must be lowercase snake case.");

    public static readonly Error WaveWindowInvalid =
        new("assignments.wave_window_invalid", "A wave must close after it opens.");

    public static readonly Error WaveNotPlanned =
        new("assignments.wave_not_planned", "Only a planned wave can open.");

    public static readonly Error WaveAlreadyClosed =
        new("assignments.wave_already_closed", "The wave is already closed.");

    public static readonly Error WaveCodeTaken =
        new("assignments.wave_code_taken", "Another wave already has this code.");

    public static readonly Error ReasonInvalid =
        new("assignments.reason_invalid", "The assignment reason is not recognised.");

    public static readonly Error ExpiryInvalid =
        new("assignments.expiry_invalid", "An assignment must expire after it is assigned.");

    public static readonly Error MemberNotActive =
        new("assignments.member_not_active", "Only an active member can be assigned an assessment.");

    public static readonly Error MemberUnder18 =
        new("assignments.member_under_18", "Members under 18 are not assigned assessments (adults only, D-58).");

    public static readonly Error VersionNotEffective =
        new("assignments.version_not_effective", "The instrument version is not approved and effective now.");

    public static readonly Error SectionNotInVersion =
        new("assignments.section_not_in_version", "The section is not part of the instrument version.");

    public static readonly Error WaveClosed =
        new("assignments.wave_closed", "The wave no longer accepts assignments.");

    public static readonly Error RuleSetWrongKind =
        new("assignments.rule_set_wrong_kind", "Assignments need an eligibility rule set.");

    public static readonly Error RuleSetNotApproved =
        new("assignments.rule_set_not_approved", "The eligibility rule set is not approved.");

    public static readonly Error RuleSetVersionMismatch =
        new("assignments.rule_set_version_mismatch", "The eligibility rule set belongs to another instrument version.");

    public static readonly Error AlreadyFinished =
        new("assignments.already_finished", "The assignment is already completed, expired or cancelled.");

    public static readonly Error NotAssigned =
        new("assignments.not_assigned", "A session can only start on an assignment that is waiting to be started.");

    public static readonly Error NotInProgress =
        new("assignments.not_in_progress", "The assignment has no session in progress.");

    public static readonly Error AssignmentExpired =
        new("assignments.assignment_expired", "The assignment has expired.");

    public static readonly Error AlreadyAssigned =
        new("assignments.already_assigned", "The member already has this assessment in this wave.");

    public static readonly Error WaveNotFound =
        new("assignments.wave_not_found", "The wave does not exist.");

    public static readonly Error WaveAmbiguous =
        new("assignments.wave_ambiguous", "More than one wave is open now, so the current wave cannot be chosen.");

    public static readonly Error AssignmentNotFound =
        new("assignments.assignment_not_found", "The assignment does not exist.");
}
