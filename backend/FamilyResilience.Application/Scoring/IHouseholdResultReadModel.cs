using FamilyResilience.Application.Abstractions;

namespace FamilyResilience.Application.Scoring;

/// <summary>The latest result of every scored session, newest first, for the household results list.</summary>
public interface IHouseholdResultReadModel
{
    Task<PagedResponse<HouseholdResultRowResponse>> ListLatestAsync(HouseholdResultsRequest request, CancellationToken cancellationToken = default);
}
