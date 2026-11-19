using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.Application.SupportResources;

internal static class SupportResourceContentFactory
{
    public static Result<SupportResourceContent> Create(ISupportResourceContentRequest request) =>
        SupportResourceContent.Create(
            request.NameAr,
            request.NameEn,
            request.SummaryAr,
            request.SummaryEn,
            request.ProviderNameAr,
            request.ProviderNameEn,
            request.Url,
            request.Categories,
            request.Audiences ?? [],
            request.ServiceLanguages,
            request.Visibility ?? throw new ArgumentException("Visibility is required.", nameof(request)));
}
