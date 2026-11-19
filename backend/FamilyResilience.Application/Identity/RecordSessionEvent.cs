using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Identity;

/// <summary>
/// Records that the signed-in caller started or ended a session in one of the platform's apps, as
/// <c>identity.session.started</c> or <c>identity.session.ended</c> with the client as the resource. The apps report
/// these themselves after the identity provider has signed the person in, so they are the platform's own trail of
/// account use; the identity provider's log stays the authoritative record of authentication.
/// </summary>
public sealed class RecordSessionEvent(IAuditLog auditLog, ICurrentUser currentUser)
{
    public const string Started = "identity.session.started";
    public const string Ended = "identity.session.ended";

    public Task<Result> HandleAsync(SessionEventKind kind, SessionEventRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.SubjectId is null)
        {
            throw new InvalidOperationException("A session record needs a signed-in caller.");
        }

        var client = request.Client ?? throw new InvalidOperationException("The validator requires a client.");
        var action = kind == SessionEventKind.Started ? Started : Ended;
        return auditLog.RecordAsync(new AuditRecord(action, AuthClients.ResourceType, client, AuditOutcome.Succeeded), cancellationToken);
    }
}
