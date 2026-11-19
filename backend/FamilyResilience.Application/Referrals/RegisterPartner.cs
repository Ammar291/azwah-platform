using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Referrals;

namespace FamilyResilience.Application.Referrals;

/// <summary>
/// Adds a partner organisation to the list support coordinators hand referrals to. A record only: it connects nothing
/// and sends the partner nothing (D-61).
/// </summary>
public sealed class RegisterPartner(
    IPartnerOrganisationRepository partners, IUnitOfWork unitOfWork, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<PartnerResponse>> HandleAsync(RegisterPartnerRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Categories);

        var name = LocalizedText.Create(request.NameAr, request.NameEn, PartnerOrganisation.MaxNameLength);
        if (name.IsFailure)
        {
            return Result.Failure<PartnerResponse>(name.Error!);
        }

        var partner = PartnerOrganisation.Register(request.Code ?? string.Empty, name.Value, request.Categories, timeProvider.GetUtcNow());
        if (partner.IsFailure)
        {
            return Result.Failure<PartnerResponse>(partner.Error!);
        }

        partners.Add(partner.Value);
        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, "referrals.partner.registered", "partner_organisation", partner.Value.Id.Value, cancellationToken);
        return committed.IsFailure
            ? Result.Failure<PartnerResponse>(committed.Error!)
            : Result.Success(PartnerResponse.From(partner.Value));
    }
}
