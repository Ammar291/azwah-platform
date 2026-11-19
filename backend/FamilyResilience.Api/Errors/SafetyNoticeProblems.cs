using Microsoft.AspNetCore.Mvc;

namespace FamilyResilience.Api.Errors;

/// <summary>
/// The crisis notice on a failure (D-138): the ProblemDetails extension <c>safetyNotice</c> beside <c>code</c> and
/// <c>traceId</c>, on any status, so a person who wrote a crisis message gets help even when the request failed.
/// </summary>
public static class SafetyNoticeProblems
{
    public const string Extension = "safetyNotice";

    /// <summary><paramref name="problem"/> with <paramref name="notice"/> added when there is one.</summary>
    public static ObjectResult WithSafetyNotice(this ObjectResult problem, string? notice)
    {
        ArgumentNullException.ThrowIfNull(problem);
        if (notice is not null && problem.Value is ProblemDetails details)
        {
            details.Extensions[Extension] = notice;
        }

        return problem;
    }
}
