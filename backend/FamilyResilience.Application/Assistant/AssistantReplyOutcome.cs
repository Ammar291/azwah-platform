using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// The reply or why there is none, and the crisis notice the answer carries either way (D-138): on a failure the API adds
/// it to the problem as the extension <c>safetyNotice</c>. Never logged: <see cref="ToString"/> prints no text.
/// </summary>
public sealed record AssistantReplyOutcome(Result<AssistantReplyResponse> Result, string? SafetyNotice)
{
    public override string ToString() =>
        $"AssistantReplyOutcome {{ IsSuccess = {Result.IsSuccess}, Error = {Result.Error?.Code}, SafetyNotice = {SafetyNotice} }}";
}
