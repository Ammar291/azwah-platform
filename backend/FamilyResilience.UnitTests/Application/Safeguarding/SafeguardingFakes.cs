using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.UnitTests.Application.Safeguarding;

/// <summary>
/// An in-memory stand-in for the database: alerts and audit events land here only when a scope's unit of work commits,
/// as a transaction would, and a failed commit discards what that scope staged. <see cref="OnCommit"/> decides each
/// commit's result (attempts are numbered from 1) and may throw; <see cref="BeforeRead"/> runs before each read of an
/// unresolved alert, so a test can make the database hang.
/// </summary>
internal sealed class SafeguardingStore
{
    public List<SafetyAlert> Alerts { get; } = [];

    public List<AuditEvent> Audits { get; } = [];

    public int CommitAttempts { get; set; }

    public Func<int, CancellationToken, Result> OnCommit { get; set; } = (_, _) => Result.Success();

    public Func<CancellationToken, Task> BeforeRead { get; set; } = _ => Task.CompletedTask;
}

/// <summary>Reads committed alerts only; adds are staged for the scope's commit.</summary>
internal sealed class StoreSafetyAlertRepository(SafeguardingStore store) : ISafetyAlertRepository
{
    public List<SafetyAlert> Staged { get; } = [];

    public Task<SafetyAlert?> GetAsync(SafetyAlertId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.Alerts.SingleOrDefault(alert => alert.Id == id));

    public async Task<SafetyAlert?> FindUnresolvedAsync(
        PlatformUserId userId, SafetyConcern concern, SafetyAlertSource source, CancellationToken cancellationToken = default)
    {
        await store.BeforeRead(cancellationToken);
        return store.Alerts.SingleOrDefault(alert =>
            alert.UserId == userId && alert.Concern == concern && alert.Source == source && alert.Status != SafetyAlertStatus.Resolved);
    }

    public Task<bool> ExistsForSessionAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.Alerts.Exists(alert => alert.AssessmentSessionId == sessionId));

    public void Add(SafetyAlert alert) => Staged.Add(alert);
}

internal sealed class StoreAuditEventRepository : IAuditEventRepository
{
    public List<AuditEvent> Staged { get; } = [];

    public void Add(AuditEvent auditEvent) => Staged.Add(auditEvent);
}

/// <summary>Commits the scope's staged alerts and audit events to the store when <see cref="SafeguardingStore.OnCommit"/> succeeds.</summary>
internal sealed class StoreUnitOfWork(SafeguardingStore store, StoreSafetyAlertRepository alerts, StoreAuditEventRepository audits) : IUnitOfWork
{
    public Task<Result> CommitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = store.OnCommit(++store.CommitAttempts, cancellationToken);
            if (result.IsSuccess)
            {
                store.Alerts.AddRange(alerts.Staged);
                store.Audits.AddRange(audits.Staged);
            }

            return Task.FromResult(result);
        }
        finally
        {
            alerts.Staged.Clear();
            audits.Staged.Clear();
        }
    }

    public Task<Result<T>> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<Result<T>>> work, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>A signal a recorder was given, with the token it came with and whether that token had already ended.</summary>
internal sealed record RecordedSignal(SafetySignal Signal, CancellationToken Token, bool TokenWasCancelled);

/// <summary>
/// A stand-in recorder for the hooks' tests: keeps every signal it is given at once and answers <see cref="Outcome"/>
/// once <see cref="Hold"/> completes, so a test can keep a recording running (a slow database).
/// </summary>
internal sealed class RecordingSafetySignalRecorder : ISafetySignalRecorder
{
    private readonly List<RecordedSignal> _calls = [];

    public SafetyAlertOutcome Outcome { get; init; } = SafetyAlertOutcome.Raised;

    public Task Hold { get; init; } = Task.CompletedTask;

    public IReadOnlyList<RecordedSignal> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.. _calls];
            }
        }
    }

    public IReadOnlyList<SafetySignal> Signals => Calls.Select(call => call.Signal).ToList();

    public async Task<SafetyAlertOutcome> RecordForCallerAsync(SafetySignal signal, CancellationToken cancellationToken)
    {
        lock (_calls)
        {
            _calls.Add(new RecordedSignal(signal, cancellationToken, cancellationToken.IsCancellationRequested));
        }

        await Hold;
        return Outcome;
    }

    public Task<SafetyAlertOutcome> RecordForSessionAsync(
        SafetyAlertSubject subject, AssessmentSessionId sessionId, SafetySignal signal, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Participant inputs record for the caller.");
}

/// <summary>The caller as the server would resolve them, with no lookups of its own.</summary>
internal sealed class FixedCallerAccess(CallerRecord? caller) : ICallerAccess
{
    public int Lookups { get; private set; }

    public Task<CallerRecord?> GetCallerAsync(CancellationToken cancellationToken = default)
    {
        Lookups++;
        return Task.FromResult(caller);
    }

    public Task<bool> CanManageHouseholdAsync(HouseholdId householdId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<bool> CanActForMemberAsync(FamilyMemberId memberId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<FamilyMemberId>> ActableMemberIdsAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>A caller lookup that never answers: it waits until its token ends. <see cref="Entered"/> says it was asked.</summary>
internal sealed class HangingCallerAccess : ICallerAccess
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Entered => _entered.Task;

    public async Task<CallerRecord?> GetCallerAsync(CancellationToken cancellationToken = default)
    {
        _entered.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return null;
    }

    public Task<bool> CanManageHouseholdAsync(HouseholdId householdId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<bool> CanActForMemberAsync(FamilyMemberId memberId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<FamilyMemberId>> ActableMemberIdsAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>
/// A clock fixed at one time whose timers fire only when the test moves time on with <see cref="Advance"/>, so a
/// time budget can run out without the test waiting for it.
/// </summary>
internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private TimeSpan _elapsed;

    public override DateTimeOffset GetUtcNow() => now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves time on and fires, once each, the timers that are now due.</summary>
    public void Advance(TimeSpan by)
    {
        List<ManualTimer> due;
        lock (_gate)
        {
            _elapsed += by;
            due = [.. _timers.Where(timer => timer.DueAt is { } at && at <= _elapsed)];
            foreach (var timer in due)
            {
                timer.DueAt = null;
            }
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    private sealed class ManualTimer(ManualTimeProvider clock, TimerCallback callback, object? state) : ITimer
    {
        public TimeSpan? DueAt { get; set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._gate)
            {
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock._elapsed + dueTime;
            }

            return true;
        }

        public void Fire() => callback(state);

        public void Dispose()
        {
            lock (clock._gate)
            {
                DueAt = null;
                clock._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
