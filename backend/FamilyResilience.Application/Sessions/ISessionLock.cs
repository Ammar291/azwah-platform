using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Sessions;

/// <summary>
/// The session's transactional update boundary. Call it first inside the explicit transaction of a submission or of
/// saving answers: it takes a row lock on the session that is held until that transaction ends, so a concurrent
/// submission or save of the same session waits and then sees what the first one committed. A session that does not
/// exist is not locked.
/// </summary>
public interface ISessionLock
{
    Task LockAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken = default);
}
