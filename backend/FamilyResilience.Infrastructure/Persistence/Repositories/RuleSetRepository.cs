using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Questionnaires;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.Repositories;

public sealed class RuleSetRepository(FamilyResilienceDbContext dbContext) : IRuleSetRepository
{
    public void Add(RuleSet ruleSet) => dbContext.RuleSets.Add(ruleSet);

    public Task<RuleSet?> GetAsync(RuleSetId id, CancellationToken cancellationToken = default) =>
        dbContext.RuleSets.SingleOrDefaultAsync(ruleSet => ruleSet.Id == id, cancellationToken);

    public async Task<IReadOnlyList<RuleSet>> ListForVersionAsync(
        InstrumentVersionId versionId, CancellationToken cancellationToken = default) =>
        await dbContext.RuleSets.AsNoTracking()
            .Where(ruleSet => ruleSet.InstrumentVersionId == versionId)
            .OrderBy(ruleSet => ruleSet.Kind)
            .ThenBy(ruleSet => ruleSet.Code)
            .ThenBy(ruleSet => ruleSet.VersionNumber)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RuleSet>> ListApprovedAsync(
        RuleSetKind kind, InstrumentVersionId versionId, CancellationToken cancellationToken = default) =>
        await dbContext.RuleSets
            .Where(ruleSet => ruleSet.Kind == kind
                && ruleSet.InstrumentVersionId == versionId
                && ruleSet.Status == RuleSetStatus.Approved)
            .OrderBy(ruleSet => ruleSet.Code)
            .ToListAsync(cancellationToken);
}
