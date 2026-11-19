using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Safeguarding;

public interface ISafetyAlertRepository
{
    /// <summary>The alert, tracked for changes.</summary>
    Task<SafetyAlert?> GetAsync(SafetyAlertId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The user's open or acknowledged alert about <paramref name="concern"/> from <paramref name="source"/>, tracked, so
    /// a repeat folds into it. At most one exists (<c>ux_safety_alerts_unresolved_subject</c>).
    /// </summary>
    Task<SafetyAlert?> FindUnresolvedAsync(
        PlatformUserId userId, SafetyConcern concern, SafetyAlertSource source, CancellationToken cancellationToken = default);

    /// <summary>Whether an alert in any status was raised for the session (<c>ux_safety_alerts_session</c>).</summary>
    Task<bool> ExistsForSessionAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken = default);

    void Add(SafetyAlert alert);
}
