using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.Repositories;

/// <summary>Row lock on the session through the scoped context, so it joins the submission's explicit transaction.</summary>
public sealed class SessionLock(FamilyResilienceDbContext dbContext) : ISessionLock
{
    public async Task LockAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("The session lock needs an explicit transaction.");
        }

        var id = sessionId.Value;
        await dbContext.Database.ExecuteSqlAsync($"SELECT id FROM assessment_sessions WHERE id = {id} FOR UPDATE", cancellationToken);
    }
}
