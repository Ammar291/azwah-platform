using FamilyResilience.Domain.Analytics;
using FamilyResilience.Domain.Common;
using FluentValidation;

namespace FamilyResilience.Application.Analytics;

/// <summary>Shape only; whether the dataset and columns are allowed is decided by <see cref="ExportCatalog"/>.</summary>
public sealed class CreateExportRequestValidator : AbstractValidator<CreateExportRequest>
{
    public CreateExportRequestValidator()
    {
        RuleFor(request => request.Dataset).NotEmpty().MaximumLength(Codes.MaxSnakeCodeLength);
        RuleFor(request => request.Columns).NotEmpty();
        RuleFor(request => request.Columns!.Count).LessThanOrEqualTo(ExportJob.MaxColumns).OverridePropertyName("columns")
            .When(request => request.Columns is not null);
        RuleForEach(request => request.Columns).NotEmpty().MaximumLength(Codes.MaxSnakeCodeLength);
        RuleFor(request => request.Filters!.InstrumentCode).MaximumLength(Codes.MaxSnakeCodeLength).OverridePropertyName("filters.instrumentCode")
            .When(request => request.Filters is not null);
    }
}
