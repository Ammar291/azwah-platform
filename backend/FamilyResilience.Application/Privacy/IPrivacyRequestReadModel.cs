using FamilyResilience.Domain.Privacy;

namespace FamilyResilience.Application.Privacy;

/// <summary>The staff view of privacy requests, with each household's reference.</summary>
public interface IPrivacyRequestReadModel
{
    /// <summary>A page of requests (of one status, or all), oldest first.</summary>
    Task<IReadOnlyList<AdminPrivacyRequestResponse>> ListAsync(
        PrivacyRequestStatus? status, int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountAsync(PrivacyRequestStatus? status, CancellationToken cancellationToken = default);

    Task<AdminPrivacyRequestResponse?> GetAsync(PrivacyRequestId id, CancellationToken cancellationToken = default);
}
