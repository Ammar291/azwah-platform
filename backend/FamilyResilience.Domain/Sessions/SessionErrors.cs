using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Sessions;

public static class SessionErrors
{
    public static readonly Error NotInProgress =
        new("sessions.not_in_progress", "The session is no longer in progress; its answers are read-only.");

    public static readonly Error VersionMismatch =
        new("sessions.version_mismatch", "The instrument version is not the one this session answers.");

    public static readonly Error AssignmentMismatch =
        new("sessions.assignment_mismatch", "The assignment is not the one this session belongs to.");

    public static readonly Error ItemNotInVersion =
        new("sessions.item_not_in_version", "The item is not part of the session's instrument version.");

    public static readonly Error ItemNotInSection =
        new("sessions.item_not_in_section", "The item is not part of the section this session answers.");

    public static readonly Error OptionNotInItem =
        new("sessions.option_not_in_item", "The option is not one of the item's options.");

    public static readonly Error RequiredItemsUnanswered =
        new("sessions.required_items_unanswered", "Every required item must be answered before submitting.");

    public static readonly Error NotSubmitted =
        new("sessions.not_submitted", "Only a submitted session can be scored.");

    public static readonly Error TimeBeforeLastChange =
        new("sessions.time_before_last_change", "The time is earlier than the session's last change.");

    public static readonly Error SessionAlreadyExists =
        new("sessions.session_already_exists", "The assignment already has a session that is not abandoned.");

    public static readonly Error DuplicateResponse =
        new("sessions.duplicate_response", "The session already has a response for this item.");

    public static readonly Error VersionNotPublished =
        new("sessions.version_not_published", "The questionnaire version is no longer published, so no new session can start.");

    public static readonly Error ParticipationConsentRequired =
        new("sessions.participation_consent_required", "The member has no active participation consent.");

    public static readonly Error SessionNotFound =
        new("sessions.session_not_found", "The session does not exist.");
}
