using FamilyResilience.Domain.Members;

namespace FamilyResilience.Application.Sessions;

/// <summary>Read-only session views, implemented in Infrastructure. Callers pass only members the caller may act for.</summary>
public interface ISessionReadModel
{
    /// <summary>One page of the members' submitted and scored sessions, most recently submitted first.</summary>
    Task<IReadOnlyList<AssessmentHistoryItemResponse>> ListHistoryAsync(
        IReadOnlyCollection<FamilyMemberId> memberIds, int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountHistoryAsync(IReadOnlyCollection<FamilyMemberId> memberIds, CancellationToken cancellationToken = default);
}
