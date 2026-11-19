using FamilyResilience.Application.Abstractions;

namespace FamilyResilience.Application.Analytics;

/// <summary>Program monitoring views, implemented in Infrastructure with database-side filtering, counting and paging.</summary>
public interface IMonitoringReadModel
{
    Task<PagedResponse<ParticipationRowResponse>> ListParticipationAsync(ParticipationRequest request, CancellationToken cancellationToken = default);

    Task<PagedResponse<AssessmentRowResponse>> ListAssessmentsAsync(AssessmentMonitoringRequest request, CancellationToken cancellationToken = default);
}
