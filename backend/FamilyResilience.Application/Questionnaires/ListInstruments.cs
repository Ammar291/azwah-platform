using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

public sealed class ListInstruments(IInstrumentRepository instruments)
{
    public async Task<Result<PagedResponse<InstrumentResponse>>> HandleAsync(PageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = await instruments.ListAsync((request.Page - 1) * request.PageSize, request.PageSize, cancellationToken);
        var total = await instruments.CountAsync(cancellationToken);
        return Result.Success(new PagedResponse<InstrumentResponse>(
            page.Select(instrument => instrument.ToResponse()).ToList(), request.Page, request.PageSize, total));
    }
}
