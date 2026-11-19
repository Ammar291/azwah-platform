using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Safeguarding;

/// <summary>
/// Records a safety signal as an alert for staff (D-135). It never throws into its caller except for the caller's own
/// cancellation: a fault is logged as a fixed code, counted, and returned as <see cref="SafetyAlertOutcome.Failed"/>.
/// It saves in a scope of its own, so the caller's pending changes are neither committed nor spoiled by it, and it bounds
/// itself (<see cref="RecordSafetySignal.RecordingLimit"/>; running out is such a failure). Never call it inside an
/// explicit transaction that locks or creates the subject's rows (their platform user, household, members or alerts):
/// its own scope would wait on that lock until the budget ran out, and the alert would be lost.
/// </summary>
public interface ISafetySignalRecorder
{
    /// <summary>
    /// The subject is the signed-in caller as the server knows them: their platform user, and their household and own
    /// member when they have one, looked up in the recorder's own scope. <see cref="SafetyAlertOutcome.Skipped"/> when
    /// the server has no record of the caller. A participant-input hook passes <see cref="CancellationToken.None"/>, not
    /// the request's token: a person who sends a crisis message and closes the app still raises an alert.
    /// </summary>
    Task<SafetyAlertOutcome> RecordForCallerAsync(SafetySignal signal, CancellationToken cancellationToken);

    /// <summary>
    /// A signal about an assessment session (a score), with the subject taken from the server's records.
    /// <see cref="SafetyAlertOutcome.Skipped"/> when an alert exists for the session in any status.
    /// </summary>
    Task<SafetyAlertOutcome> RecordForSessionAsync(
        SafetyAlertSubject subject, AssessmentSessionId sessionId, SafetySignal signal, CancellationToken cancellationToken);
}
