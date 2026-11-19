using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

public interface IRuleSetRepository
{
    void Add(RuleSet ruleSet);

    Task<RuleSet?> GetAsync(RuleSetId id, CancellationToken cancellationToken = default);

    /// <summary>Every rule set of one version, whatever its status, by kind, code and version number.</summary>
    Task<IReadOnlyList<RuleSet>> ListForVersionAsync(InstrumentVersionId versionId, CancellationToken cancellationToken = default);

    /// <summary>Approved rule sets of one kind for one version. Several codes may be approved at once.</summary>
    Task<IReadOnlyList<RuleSet>> ListApprovedAsync(
        RuleSetKind kind, InstrumentVersionId versionId, CancellationToken cancellationToken = default);
}
