using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Privacy;

public static class PrivacyErrors
{
    public static readonly Error RequestNotFound =
        new("privacy.request_not_found", "The privacy request does not exist.");

    public static readonly Error RequestAlreadyOpen =
        new("privacy.request_already_open", "A request of this type is already open for this member.");

    public static readonly Error RequestAlreadyClosed =
        new("privacy.request_already_closed", "The privacy request is already closed.");

    public static readonly Error TypeInvalid =
        new("privacy.type_invalid", "The request type must be access, correction or erasure.");

    public static readonly Error ResolutionInvalid =
        new("privacy.resolution_invalid", "The resolution must be completed, declined or withdrawn.");

    public static readonly Error ActorInvalid =
        new("privacy.actor_invalid", "The subject who requests or closes must be 1 to 200 characters.");
}
