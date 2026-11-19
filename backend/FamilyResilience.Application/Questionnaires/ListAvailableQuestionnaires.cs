using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Questionnaires;

/// <summary>Every active instrument that has an approved version in effect now, by instrument code, a page at a time.</summary>
public sealed class ListAvailableQuestionnaires(
    IInstrumentRepository instruments, IInstrumentVersionRepository versions, TimeProvider timeProvider)
{
    public async Task<Result<PagedResponse<AvailableQuestionnaireResponse>>> HandleAsync(
        PageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = timeProvider.GetUtcNow();
        var available = new List<AvailableQuestionnaireResponse>();
        foreach (var instrument in await instruments.ListActiveAsync(cancellationToken))
        {
            if (await versions.GetEffectiveApprovedAsync(instrument.Id, now, cancellationToken) is { } version)
            {
                available.Add(version.ToAvailable(instrument));
            }
        }

        return Result.Success(new PagedResponse<AvailableQuestionnaireResponse>(
            available.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToList(),
            request.Page,
            request.PageSize,
            available.Count));
    }
}
