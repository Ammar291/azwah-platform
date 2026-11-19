using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.Repositories;

public sealed class SafetyAlertRepository(FamilyResilienceDbContext dbContext) : ISafetyAlertRepository
{
    public void Add(SafetyAlert alert) => dbContext.SafetyAlerts.Add(alert);

    public Task<SafetyAlert?> GetAsync(SafetyAlertId id, CancellationToken cancellationToken = default) =>
        dbContext.SafetyAlerts.SingleOrDefaultAsync(alert => alert.Id == id, cancellationToken);

    public Task<SafetyAlert?> FindUnresolvedAsync(
        PlatformUserId userId, SafetyConcern concern, SafetyAlertSource source, CancellationToken cancellationToken = default) =>
        dbContext.SafetyAlerts.SingleOrDefaultAsync(
            alert => alert.UserId == userId && alert.Concern == concern && alert.Source == source
                && alert.Status != SafetyAlertStatus.Resolved,
            cancellationToken);

    public Task<bool> ExistsForSessionAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken = default) =>
        dbContext.SafetyAlerts.AnyAsync(alert => alert.AssessmentSessionId == sessionId, cancellationToken);
}
