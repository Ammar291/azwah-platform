using FamilyResilience.Application.Consent;
using FamilyResilience.Domain.Consent;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.Repositories;

public sealed class ConsentPolicyVersionRepository(FamilyResilienceDbContext dbContext) : IConsentPolicyVersionRepository
{
    public void Add(ConsentPolicyVersion policy) => dbContext.ConsentPolicyVersions.Add(policy);

    public Task<ConsentPolicyVersion?> GetAsync(ConsentPolicyVersionId id, CancellationToken cancellationToken = default) =>
        dbContext.ConsentPolicyVersions.SingleOrDefaultAsync(policy => policy.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ConsentPolicyVersion>> ListCurrentAsync(CancellationToken cancellationToken = default)
    {
        var approved = await dbContext.ConsentPolicyVersions
            .Where(policy => policy.Status == ConsentPolicyStatus.Approved)
            .ToListAsync(cancellationToken);
        return approved
            .GroupBy(policy => policy.ConsentType)
            .Select(group => group.MaxBy(policy => policy.VersionNumber)!)
            .OrderBy(policy => policy.ConsentType)
            .ToList();
    }
}
