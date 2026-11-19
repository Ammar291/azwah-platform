using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.Domain.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FamilyResilience.Application.Safeguarding;

/// <summary>
/// The safety signal recorder (D-135). An unresolved alert for the same user, concern and source takes the signal as a
/// repeat (<see cref="SafetyAlert.Recur"/>, which re-opens an acknowledged alert; audited <see cref="Repeated"/> and
/// counted); otherwise a new alert is raised (audited <see cref="Raised"/> and counted). Both are audited as the System
/// actor, never the participant: the detector acted, and a trail of crisis signals under the person's subject must not
/// outlive the erasure of their alerts. Each attempt runs in a DI scope of its own, so a failed save never leaves changes
/// behind in the caller's unit of work, and a retry starts clean. A lost race (another request inserted the same alert
/// first, or changed it first) is read again once and folded into the alert that won. Any other fault, and a second
/// lost race, is logged as the fixed code <see cref="FailureCode"/> with the exception's type or the refused error's
/// code, counted, and returned as <see cref="SafetyAlertOutcome.Failed"/>; only the caller's own cancellation
/// propagates. A recording has <see cref="RecordingLimit"/> in all: past it the work is cancelled, and that is a failure
/// like any other, logged and counted. A caller the server has no record of is skipped and counted. The caller is looked
/// up in each attempt's own scope, never in the request's, so a budget that ends during the lookup leaves nothing the
/// request still uses. Nothing here sees text, and no ID or exception message reaches a log.
/// </summary>
public sealed partial class RecordSafetySignal(
    IServiceScopeFactory scopes,
    SafeguardingMetrics metrics,
    TimeProvider timeProvider,
    ILogger<RecordSafetySignal> logger) : ISafetySignalRecorder
{
    public const string Raised = "safeguarding.alert.raised";
    public const string Repeated = "safeguarding.alert.repeated";
    public const string ResourceType = "safety_alert";
    public const string FailureCode = "safety_alert_failed";

    /// <summary>The most one recording may take, attempts and retry included, so it never holds a person's request long.</summary>
    public static readonly TimeSpan RecordingLimit = TimeSpan.FromSeconds(5);

    public Task<SafetyAlertOutcome> RecordForCallerAsync(SafetySignal signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);

        // On this path a skip can only mean the server has no record of the caller: a signal nobody will see, so counted.
        return GuardedAsync(
            signal, budget => RecordAsync(CallerSubjectAsync, null, signal, budget), countSkipped: true, cancellationToken);
    }

    public Task<SafetyAlertOutcome> RecordForSessionAsync(
        SafetyAlertSubject subject, AssessmentSessionId sessionId, SafetySignal signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(signal);
        return GuardedAsync(
            signal,
            budget => RecordAsync((_, _) => Task.FromResult<SafetyAlertSubject?>(subject), sessionId, signal, budget),
            countSkipped: false,
            cancellationToken);
    }

    /// <summary>The signed-in caller, from the attempt's own scope: their user, household and own active member.</summary>
    private static async Task<SafetyAlertSubject?> CallerSubjectAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var caller = await services.GetRequiredService<ICallerAccess>().GetCallerAsync(cancellationToken);
        if (caller is null)
        {
            return null;
        }

        var household = caller.Household;
        return new SafetyAlertSubject(caller.UserId, household?.Id, household?.FindActiveMembership(caller.UserId)?.FamilyMemberId);
    }

    private async Task<SafetyAlertOutcome> GuardedAsync(
        SafetySignal signal,
        Func<CancellationToken, Task<Result<SafetyAlertOutcome>>> record,
        bool countSkipped,
        CancellationToken cancellationToken)
    {
        using var budget = new CancellationTokenSource(RecordingLimit, timeProvider);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
        SafetyAlertOutcome outcome;
        string? reason = null;
        try
        {
            var recorded = await record(bounded.Token);
            (outcome, reason) = recorded.IsSuccess ? (recorded.Value, null) : (SafetyAlertOutcome.Failed, recorded.Error!.Code);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Broad on purpose: recording must never fail the person's request (D-135), and the budget running out is a
            // failure too. Only the exception's type is logged, because a database message can quote row values.
            (outcome, reason) = (SafetyAlertOutcome.Failed, exception.GetType().Name);
        }

        var severity = SafetyAlert.SeverityOf(signal.Concern);
        switch (outcome)
        {
            case SafetyAlertOutcome.Failed:
                LogRecordingFailed(logger, FailureCode, reason!);
                metrics.RecordingFailed(signal.Source);
                break;
            case SafetyAlertOutcome.Raised:
                metrics.AlertRaised(signal.Concern, severity, signal.Source);
                break;
            case SafetyAlertOutcome.Repeated:
                metrics.AlertRepeated(signal.Concern, severity, signal.Source);
                break;
            case SafetyAlertOutcome.Skipped when countSkipped:
                metrics.RecordingSkipped(signal.Source);
                break;
        }

        return outcome;
    }

    private async Task<Result<SafetyAlertOutcome>> RecordAsync(
        SubjectOf subjectOf, AssessmentSessionId? sessionId, SafetySignal signal, CancellationToken cancellationToken)
    {
        var attempt = await AttemptAsync(subjectOf, sessionId, signal, cancellationToken);
        if (attempt.IsFailure && (attempt.Error == PersistenceErrors.Conflict || attempt.Error == PersistenceErrors.ConcurrencyConflict))
        {
            attempt = await AttemptAsync(subjectOf, sessionId, signal, cancellationToken);
        }

        return attempt;
    }

    private async Task<Result<SafetyAlertOutcome>> AttemptAsync(
        SubjectOf subjectOf, AssessmentSessionId? sessionId, SafetySignal signal, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        if (await subjectOf(services, cancellationToken) is not { } subject)
        {
            return Result.Success(SafetyAlertOutcome.Skipped);
        }

        var alerts = services.GetRequiredService<ISafetyAlertRepository>();
        if (sessionId is { } session && await alerts.ExistsForSessionAsync(session, cancellationToken))
        {
            return Result.Success(SafetyAlertOutcome.Skipped);
        }

        var now = timeProvider.GetUtcNow();
        var unresolved = subject.UserId is { } userId
            ? await alerts.FindUnresolvedAsync(userId, signal.Concern, signal.Source, cancellationToken)
            : null;
        SafetyAlert alert;
        SafetyAlertOutcome outcome;
        if (unresolved is not null)
        {
            var recurred = unresolved.Recur(signal.Detector, signal.SignalCode, now);
            if (recurred.IsFailure)
            {
                return Result.Failure<SafetyAlertOutcome>(recurred.Error!);
            }

            (alert, outcome) = (unresolved, SafetyAlertOutcome.Repeated);
        }
        else
        {
            alert = SafetyAlert.Raise(subject, signal.Source, signal.Concern, signal.Detector, signal.SignalCode, signal.Language, sessionId, now);
            alerts.Add(alert);
            outcome = SafetyAlertOutcome.Raised;
        }

        var committed = await AuditedCommit.RunAsync(
            services.GetRequiredService<IAuditLog>(),
            services.GetRequiredService<IUnitOfWork>(),
            new AuditRecord(
                outcome == SafetyAlertOutcome.Raised ? Raised : Repeated, ResourceType, alert.Id.Value.ToString(), AuditOutcome.Succeeded)
            {
                BySystem = true,
            },
            cancellationToken);
        return committed.IsFailure ? Result.Failure<SafetyAlertOutcome>(committed.Error!) : Result.Success(outcome);
    }

    /// <summary><paramref name="reason"/> is an exception's type name or a refused error's code: fixed, never a message.</summary>
    [LoggerMessage(EventId = 1600, Level = LogLevel.Error, Message = "Safety alert recording failed ({Code}, {Reason})")]
    private static partial void LogRecordingFailed(ILogger logger, string code, string reason);

    /// <summary>Who the alert is about, read from an attempt's own scope; null when the server has no record of them.</summary>
    private delegate Task<SafetyAlertSubject?> SubjectOf(IServiceProvider services, CancellationToken cancellationToken);
}
