using FamilyResilience.Domain.Consent;

namespace FamilyResilience.Application.Consent;

public interface IConsentPolicyVersionRepository
{
    void Add(ConsentPolicyVersion policy);

    Task<ConsentPolicyVersion?> GetAsync(ConsentPolicyVersionId id, CancellationToken cancellationToken = default);

    /// <summary>The current policy of each consent type: its highest approved, not retired, version. At most one per type.</summary>
    Task<IReadOnlyList<ConsentPolicyVersion>> ListCurrentAsync(CancellationToken cancellationToken = default);
}
