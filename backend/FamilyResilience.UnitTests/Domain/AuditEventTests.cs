using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.UnitTests.Domain;

public sealed class AuditEventTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 30, 0, TimeSpan.Zero);

    private static Result<AuditEvent> Create(
        DateTimeOffset? occurredAt = null,
        AuditActorType actorType = AuditActorType.User,
        string? actorId = "subject-123",
        string action = "system.health.checked",
        string resourceType = "system",
        string? resourceId = null,
        AuditOutcome outcome = AuditOutcome.Succeeded,
        string? correlationId = null) =>
        AuditEvent.Create(occurredAt ?? Now, actorType, actorId, action, resourceType, resourceId, outcome, correlationId);

    [Fact]
    public void Valid_user_event_is_created_with_a_version_7_id()
    {
        var result = Create(resourceId: "household-1", correlationId: "4bf92f3577b34da6a3ce929d0e0e4736");

        Assert.True(result.IsSuccess);
        var auditEvent = result.Value;
        Assert.Equal(7, auditEvent.Id.Value.Version);
        Assert.Equal(Now, auditEvent.OccurredAt);
        Assert.Equal(AuditActorType.User, auditEvent.ActorType);
        Assert.Equal("subject-123", auditEvent.ActorId);
        Assert.Equal("system.health.checked", auditEvent.Action);
        Assert.Equal("system", auditEvent.ResourceType);
        Assert.Equal("household-1", auditEvent.ResourceId);
        Assert.Equal(AuditOutcome.Succeeded, auditEvent.Outcome);
        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", auditEvent.CorrelationId);
    }

    [Fact]
    public void Time_must_be_utc() =>
        Assert.Equal(
            AuditErrors.OccurredAtNotUtc,
            Create(occurredAt: new DateTimeOffset(2026, 10, 11, 13, 30, 0, TimeSpan.FromHours(4))).Error);

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("system")]
    [InlineData("System.Health")]
    [InlineData("system..checked")]
    [InlineData("system.health-checked")]
    [InlineData(".system.health")]
    public void Action_must_be_a_dotted_lowercase_code(string action) =>
        Assert.Equal(AuditErrors.ActionInvalid, Create(action: action).Error);

    [Fact]
    public void Action_longer_than_100_characters_is_rejected() =>
        Assert.Equal(AuditErrors.ActionInvalid, Create(action: new string('a', 50) + "." + new string('b', 50)).Error);

    [Theory]
    [InlineData("system.health.checked")]
    [InlineData("consent.granted")]
    [InlineData("member_module.skipped")]
    public void Dotted_lowercase_actions_are_accepted(string action) =>
        Assert.True(Create(action: action).IsSuccess);

    [Fact]
    public void Resource_type_is_required() =>
        Assert.Equal(AuditErrors.ResourceTypeInvalid, Create(resourceType: " ").Error);

    [Fact]
    public void Resource_type_longer_than_100_characters_is_rejected() =>
        Assert.Equal(AuditErrors.ResourceTypeInvalid, Create(resourceType: new string('r', 101)).Error);

    [Fact]
    public void Resource_id_longer_than_200_characters_is_rejected() =>
        Assert.Equal(AuditErrors.ResourceIdTooLong, Create(resourceId: new string('x', 201)).Error);

    [Fact]
    public void User_actor_needs_an_actor_id() =>
        Assert.Equal(AuditErrors.ActorIdRequired, Create(actorType: AuditActorType.User, actorId: null).Error);

    [Fact]
    public void Anonymous_actor_cannot_have_an_actor_id() =>
        Assert.Equal(AuditErrors.ActorIdNotAllowed, Create(actorType: AuditActorType.Anonymous, actorId: "someone").Error);

    [Fact]
    public void System_actor_may_omit_the_actor_id() =>
        Assert.True(Create(actorType: AuditActorType.System, actorId: null).IsSuccess);

    [Fact]
    public void Actor_id_longer_than_200_characters_is_rejected() =>
        Assert.Equal(AuditErrors.ActorIdTooLong, Create(actorId: new string('s', 201)).Error);

    [Fact]
    public void Correlation_id_longer_than_100_characters_is_rejected() =>
        Assert.Equal(AuditErrors.CorrelationIdTooLong, Create(correlationId: new string('c', 101)).Error);

    [Fact]
    public void Undefined_actor_type_is_rejected() =>
        Assert.Equal(AuditErrors.ActorTypeInvalid, Create(actorType: (AuditActorType)99).Error);

    [Fact]
    public void Undefined_outcome_is_rejected() =>
        Assert.Equal(AuditErrors.OutcomeInvalid, Create(outcome: (AuditOutcome)0).Error);
}
