using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

/// <summary>An instrument's versions without their content, newest version first, for the read-only configuration view.</summary>
public sealed class ListInstrumentVersions(IInstrumentRepository instruments, IInstrumentVersionRepository versions)
{
    public async Task<Result<PagedResponse<InstrumentVersionSummaryResponse>>> HandleAsync(
        Guid instrumentId, PageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = InstrumentId.From(instrumentId);
        if (await instruments.GetAsync(id, cancellationToken) is null)
        {
            return Result.Failure<PagedResponse<InstrumentVersionSummaryResponse>>(QuestionnaireErrors.InstrumentNotFound);
        }

        var page = await versions.ListSummariesAsync(id, (request.Page - 1) * request.PageSize, request.PageSize, cancellationToken);
        var total = await versions.CountAsync(id, cancellationToken);
        return Result.Success(new PagedResponse<InstrumentVersionSummaryResponse>(page, request.Page, request.PageSize, total));
    }
}
