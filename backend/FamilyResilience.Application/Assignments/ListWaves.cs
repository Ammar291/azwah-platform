using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Assignments;

/// <summary>Waves for staff filters, newest opening first. Waves hold no personal data, so reads are not audited.</summary>
public sealed class ListWaves(IWaveRepository waves)
{
    public async Task<Result<PagedResponse<WaveResponse>>> HandleAsync(PageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = await waves.ListAsync((request.Page - 1) * request.PageSize, request.PageSize, cancellationToken);
        var total = await waves.CountAsync(cancellationToken);
        return Result.Success(new PagedResponse<WaveResponse>(
            page.Select(wave => new WaveResponse(wave.Id.Value, wave.Code, wave.Status, wave.OpensAt, wave.ClosesAt)).ToList(),
            request.Page,
            request.PageSize,
            total));
    }
}
