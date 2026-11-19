using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// Calls a report model under a time limit and audits that facts left the platform (the session, the outcome, no text),
/// also when the person leaves while it writes. Any failure, fault, time-out or unusable text is null; the exception is
/// dropped unlogged, because an adapter's message could quote the request.
/// </summary>
public sealed class ScoreReportModelCalls(IAuditLog auditLog, AssistantLimits limits)
{
    public async Task<T?> CallAsync<T>(
        string auditAction, Guid sessionId, Func<CancellationToken, Task<Result<T>>> write, Func<T, bool> isUsable, CancellationToken cancellationToken)
        where T : class
    {
        T? writing = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limits.ModelTimeout);
        try
        {
            // WaitAsync bounds the call even when an adapter ignores its token.
            var written = await write(timeout.Token).WaitAsync(timeout.Token);
            writing = written.IsSuccess && isUsable(written.Value) ? written.Value : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await auditLog.RecordAsync(new AuditRecord(auditAction, "assessment_session", sessionId.ToString(), AuditOutcome.Failed), CancellationToken.None);
            throw;
        }
        catch (Exception)
        {
            // See the summary: broad on purpose, and never logged.
        }

        var recorded = await auditLog.RecordAsync(
            new AuditRecord(auditAction, "assessment_session", sessionId.ToString(), writing is null ? AuditOutcome.Failed : AuditOutcome.Succeeded),
            cancellationToken);
        return recorded.IsSuccess ? writing : null;
    }
}
