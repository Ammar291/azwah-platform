using FamilyResilience.Domain.Privacy;

namespace FamilyResilience.Application.Privacy;

internal static class PrivacyMappings
{
    public static PrivacyRequestResponse ToResponse(this PrivacyRequest request) =>
        new(request.Id.Value, request.FamilyMemberId.Value, request.Type, request.Status, request.CreatedAt, request.ClosedAt, request.Resolution);
}
