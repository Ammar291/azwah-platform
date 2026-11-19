using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.Application.SupportResources;

public sealed record SupportResourceResponse(
    Guid Id,
    string Code,
    int VersionNumber,
    SupportResourceStatus Status,
    SupportResourceVisibility Visibility,
    string? NameAr,
    string NameEn,
    string? SummaryAr,
    string SummaryEn,
    string? ProviderNameAr,
    string ProviderNameEn,
    string Url,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Audiences,
    IReadOnlyList<string> ServiceLanguages,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    string? ApprovalSource,
    DateTimeOffset? ReviewDueAt,
    bool ReviewOverdue,
    DateTimeOffset? RetiredAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static SupportResourceResponse From(SupportResource resource, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return new SupportResourceResponse(
            resource.Id.Value,
            resource.Code,
            resource.VersionNumber,
            resource.Status,
            resource.Visibility,
            resource.Name.Arabic,
            resource.Name.English,
            resource.Summary.Arabic,
            resource.Summary.English,
            resource.ProviderName.Arabic,
            resource.ProviderName.English,
            resource.Url,
            resource.Categories,
            resource.Audiences,
            resource.ServiceLanguages,
            resource.ApprovedBy,
            resource.ApprovedAt,
            resource.ApprovalSource,
            resource.ReviewDueAt,
            resource.Status == SupportResourceStatus.Approved && resource.ReviewDueAt <= now,
            resource.RetiredAt,
            resource.CreatedAt,
            resource.UpdatedAt);
    }
}
