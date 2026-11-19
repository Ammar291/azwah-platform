using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.SupportResources;

/// <summary>
/// A participant's directory search as the API answers it: the result, and the crisis notice (D-138) that the API adds
/// to a failure's ProblemDetails as the extension <c>safetyNotice</c> (a success carries it in its body). Never logged:
/// <see cref="ToString"/> prints codes only.
/// </summary>
public sealed record SupportSearchOutcome(Result<SupportResourceDiscoveryResponse> Result, string? SafetyNotice)
{
    public override string ToString() =>
        $"SupportSearchOutcome {{ IsSuccess = {Result.IsSuccess}, Error = {Result.Error?.Code}, SafetyNotice = {SafetyNotice} }}";
}
