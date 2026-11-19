using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Consent;

public static class ConsentErrors
{
    public static readonly Error ConsentTypeInvalid =
        new("consent.consent_type_invalid", "The consent type is not recognised.");

    public static readonly Error VersionNumberInvalid =
        new("consent.version_number_invalid", "The policy version number must be 1 or more.");

    public static readonly Error DocumentReferenceInvalid =
        new("consent.document_reference_invalid", "Both document references are required and at most 300 characters.");

    public static readonly Error PolicyNotDraft =
        new("consent.policy_not_draft", "Only a draft consent policy can be approved.");

    public static readonly Error PolicyNotApproved =
        new("consent.policy_not_approved", "The consent policy is not approved.");

    public static readonly Error PolicyVersionExists =
        new("consent.policy_version_exists", "This consent type already has a policy with that version number.");

    public static readonly Error CaptureMethodInvalid =
        new("consent.capture_method_invalid", "The capture method is not recognised.");

    public static readonly Error MemberNotActive =
        new("consent.member_not_active", "Consent can only be recorded for an active member.");

    public static readonly Error ActorIdTooLong =
        new("consent.actor_id_too_long", "The actor ID must be at most 200 characters.");

    public static readonly Error NotActive =
        new("consent.not_active", "The consent has already been withdrawn.");

    public static readonly Error WithdrawnBeforeGranted =
        new("consent.withdrawn_before_granted", "Consent cannot be withdrawn before it was granted.");

    public static readonly Error PolicyNotFound =
        new("consent.policy_not_found", "The consent policy version does not exist.");

    public static readonly Error PolicyNotCurrent =
        new("consent.policy_not_current", "Consent can only be given to the current approved version of its policy.");

    public static readonly Error MemberUnder18 =
        new("consent.member_under_18", "Only members aged 18 or over give consent.");

    public static readonly Error AlreadyActive =
        new("consent.already_active", "The member already has an active consent of this type.");
}
