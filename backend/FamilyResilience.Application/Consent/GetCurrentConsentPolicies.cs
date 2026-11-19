using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Consent;

public sealed class GetCurrentConsentPolicies(IConsentPolicyVersionRepository policies)
{
    public async Task<Result<CurrentConsentPoliciesResponse>> HandleAsync(CancellationToken cancellationToken) =>
        Result.Success(new CurrentConsentPoliciesResponse(
            (await policies.ListCurrentAsync(cancellationToken)).Select(policy => policy.ToResponse()).ToList()));
}
