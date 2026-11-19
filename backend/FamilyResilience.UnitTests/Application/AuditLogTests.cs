using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Audit;

namespace FamilyResilience.UnitTests.Application;

public sealed class AuditLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 30, 0, TimeSpan.Zero);
    private readonly FakeAuditEventRepository _repository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private AuditLog CreateLog(FakeCurrentUser user) => new(_repository, _unitOfWork, user, new FixedTimeProvider(Now));

    [Fact]
    public async Task Records_a_signed_in_caller_as_a_user_actor()
    {
        var log = CreateLog(new FakeCurrentUser(true, "subject-7", "researcher"));

        var result = await log.RecordAsync(
            new AuditRecord("consent.granted", "consent", "consent-1", AuditOutcome.Succeeded),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var stored = Assert.Single(_repository.Added);
        Assert.Equal(AuditActorType.User, stored.ActorType);
        Assert.Equal("subject-7", stored.ActorId);
        Assert.Equal(Now, stored.OccurredAt);
        Assert.Equal("consent.granted", stored.Action);
        Assert.Equal(1, _unitOfWork.CommitCount);
    }

    [Fact]
    public async Task Records_a_caller_without_identity_as_anonymous()
    {
        var log = CreateLog(new FakeCurrentUser(false, null));

        var result = await log.RecordAsync(
            new AuditRecord("system.health.checked", "system", null, AuditOutcome.Succeeded),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var stored = Assert.Single(_repository.Added);
        Assert.Equal(AuditActorType.Anonymous, stored.ActorType);
        Assert.Null(stored.ActorId);
    }

    [Fact]
    public async Task Records_the_platform_acting_outside_a_request_as_the_system()
    {
        var log = CreateLog(new FakeCurrentUser(false, null) { IsSystem = true });

        var result = await log.RecordAsync(
            new AuditRecord("assignments.eligibility.evaluated", "household", "household-1", AuditOutcome.Succeeded),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var stored = Assert.Single(_repository.Added);
        Assert.Equal(AuditActorType.System, stored.ActorType);
        Assert.Null(stored.ActorId);
    }

    [Fact]
    public async Task Records_a_record_by_the_system_as_the_system_even_inside_a_signed_in_request()
    {
        var log = CreateLog(new FakeCurrentUser(true, "subject-7", "participant"));

        var result = await log.RecordAsync(
            new AuditRecord("safeguarding.alert.raised", "safety_alert", "alert-1", AuditOutcome.Succeeded) { BySystem = true },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var stored = Assert.Single(_repository.Added);
        Assert.Equal((AuditActorType.System, (string?)null), (stored.ActorType, stored.ActorId));
    }

    [Fact]
    public async Task Invalid_record_is_rejected_and_nothing_is_saved()
    {
        var log = CreateLog(new FakeCurrentUser(true, "subject-7"));

        var result = await log.RecordAsync(
            new AuditRecord("Not A Code", "system", null, AuditOutcome.Succeeded),
            TestContext.Current.CancellationToken);

        Assert.Equal(AuditErrors.ActionInvalid, result.Error);
        Assert.Empty(_repository.Added);
        Assert.Equal(0, _unitOfWork.CommitCount);
    }

    [Fact]
    public void Stage_adds_the_event_without_committing()
    {
        var log = CreateLog(new FakeCurrentUser(true, "subject-7"));

        var result = log.Stage(new AuditRecord("consent.granted", "consent", "consent-1", AuditOutcome.Succeeded));

        Assert.True(result.IsSuccess);
        Assert.Single(_repository.Added);
        Assert.Equal(0, _unitOfWork.CommitCount);
    }

    [Fact]
    public void Stage_rejects_an_invalid_record_and_adds_nothing()
    {
        var log = CreateLog(new FakeCurrentUser(true, "subject-7"));

        var result = log.Stage(new AuditRecord("Not A Code", "consent", null, AuditOutcome.Succeeded));

        Assert.Equal(AuditErrors.ActionInvalid, result.Error);
        Assert.Empty(_repository.Added);
    }
}
