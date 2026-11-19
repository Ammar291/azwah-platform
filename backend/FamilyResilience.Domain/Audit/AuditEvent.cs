using System.Text.RegularExpressions;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Audit;

/// <summary>
/// An append-only record of something that happened. It deliberately has no free-text field, so participant
/// answers and other personal details cannot end up in the audit log.
/// </summary>
public sealed partial class AuditEvent : Entity<AuditEventId>
{
    public const int MaxActionLength = 100;
    public const int MaxResourceTypeLength = 100;
    public const int MaxResourceIdLength = 200;
    public const int MaxActorIdLength = 200;
    public const int MaxCorrelationIdLength = 100;

    // Parameter names match property names so EF Core can bind this constructor when reading rows.
    private AuditEvent(
        AuditEventId id,
        DateTimeOffset occurredAt,
        AuditActorType actorType,
        string? actorId,
        string action,
        string resourceType,
        string? resourceId,
        AuditOutcome outcome,
        string? correlationId)
        : base(id)
    {
        OccurredAt = occurredAt;
        ActorType = actorType;
        ActorId = actorId;
        Action = action;
        ResourceType = resourceType;
        ResourceId = resourceId;
        Outcome = outcome;
        CorrelationId = correlationId;
    }

    public DateTimeOffset OccurredAt { get; private set; }

    public AuditActorType ActorType { get; private set; }

    public string? ActorId { get; private set; }

    public string Action { get; private set; }

    public string ResourceType { get; private set; }

    public string? ResourceId { get; private set; }

    public AuditOutcome Outcome { get; private set; }

    public string? CorrelationId { get; private set; }

    public static Result<AuditEvent> Create(
        DateTimeOffset occurredAt,
        AuditActorType actorType,
        string? actorId,
        string action,
        string resourceType,
        string? resourceId,
        AuditOutcome outcome,
        string? correlationId)
    {
        if (occurredAt.Offset != TimeSpan.Zero)
        {
            return Result.Failure<AuditEvent>(AuditErrors.OccurredAtNotUtc);
        }

        if (!Enum.IsDefined(actorType))
        {
            return Result.Failure<AuditEvent>(AuditErrors.ActorTypeInvalid);
        }

        if (!Enum.IsDefined(outcome))
        {
            return Result.Failure<AuditEvent>(AuditErrors.OutcomeInvalid);
        }

        if (string.IsNullOrWhiteSpace(action) || action.Length > MaxActionLength || !ActionCode().IsMatch(action))
        {
            return Result.Failure<AuditEvent>(AuditErrors.ActionInvalid);
        }

        if (string.IsNullOrWhiteSpace(resourceType) || resourceType.Length > MaxResourceTypeLength)
        {
            return Result.Failure<AuditEvent>(AuditErrors.ResourceTypeInvalid);
        }

        if (resourceId is { Length: > MaxResourceIdLength })
        {
            return Result.Failure<AuditEvent>(AuditErrors.ResourceIdTooLong);
        }

        if (actorType == AuditActorType.User && string.IsNullOrWhiteSpace(actorId))
        {
            return Result.Failure<AuditEvent>(AuditErrors.ActorIdRequired);
        }

        if (actorType == AuditActorType.Anonymous && actorId is not null)
        {
            return Result.Failure<AuditEvent>(AuditErrors.ActorIdNotAllowed);
        }

        if (actorId is { Length: > MaxActorIdLength })
        {
            return Result.Failure<AuditEvent>(AuditErrors.ActorIdTooLong);
        }

        if (correlationId is { Length: > MaxCorrelationIdLength })
        {
            return Result.Failure<AuditEvent>(AuditErrors.CorrelationIdTooLong);
        }

        return Result.Success(new AuditEvent(
            AuditEventId.New(), occurredAt, actorType, actorId, action, resourceType, resourceId, outcome, correlationId));
    }

    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex ActionCode();
}
