using FamilyResilience.Domain.Referrals;

namespace FamilyResilience.Application.Referrals;

public interface IPartnerOrganisationRepository
{
    void Add(PartnerOrganisation partner);

    Task<PartnerOrganisation?> GetAsync(PartnerOrganisationId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The partner, read after taking a share lock on its row, so a deactivation waits until the caller's transaction
    /// ends and one that committed first is seen. Call it inside <c>IUnitOfWork.ExecuteInTransactionAsync</c>.
    /// </summary>
    Task<PartnerOrganisation?> GetWithShareLockAsync(PartnerOrganisationId id, CancellationToken cancellationToken = default);

    /// <summary>Partners by code, a page at a time, read without tracking.</summary>
    Task<IReadOnlyList<PartnerOrganisation>> ListAsync(int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PartnerOrganisation>> ListByIdsAsync(
        IReadOnlyCollection<PartnerOrganisationId> ids, CancellationToken cancellationToken = default);
}
