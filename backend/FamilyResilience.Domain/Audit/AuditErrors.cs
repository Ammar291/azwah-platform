using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Audit;

public static class AuditErrors
{
    public static readonly Error OccurredAtNotUtc =
        new("audit.occurred_at_not_utc", "The time an audit event occurred must be in UTC.");

    public static readonly Error ActionInvalid =
        new("audit.action_invalid", "The action must be a dotted lowercase code of at most 100 characters, such as 'system.health.checked'.");

    public static readonly Error ResourceTypeInvalid =
        new("audit.resource_type_invalid", "The resource type is required and must be at most 100 characters.");

    public static readonly Error ResourceIdTooLong =
        new("audit.resource_id_too_long", "The resource ID must be at most 200 characters.");

    public static readonly Error ActorTypeInvalid =
        new("audit.actor_type_invalid", "The actor type is not recognised.");

    public static readonly Error ActorIdRequired =
        new("audit.actor_id_required", "A user actor must have an actor ID.");

    public static readonly Error ActorIdNotAllowed =
        new("audit.actor_id_not_allowed", "An anonymous actor cannot have an actor ID.");

    public static readonly Error ActorIdTooLong =
        new("audit.actor_id_too_long", "The actor ID must be at most 200 characters.");

    public static readonly Error OutcomeInvalid =
        new("audit.outcome_invalid", "The outcome is not recognised.");

    public static readonly Error CorrelationIdTooLong =
        new("audit.correlation_id_too_long", "The correlation ID must be at most 100 characters.");
}
