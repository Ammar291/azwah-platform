using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FamilyResilience.Api.Requests;

/// <summary>
/// The <c>Idempotency-Key</c> request header: a client-generated UUID, required on submissions
/// (docs/api-conventions.md). A missing or malformed key answers 400 keyed <c>idempotencyKey</c>.
/// </summary>
internal static class IdempotencyHeader
{
    public const string Name = "Idempotency-Key";

    public static bool TryParse(string? value, out Guid key) =>
        Guid.TryParse(value, out key) && key != Guid.Empty;

    public static ObjectResult Invalid(ControllerBase controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var errors = new ModelStateDictionary();
        errors.AddModelError("idempotencyKey", $"The {Name} header must be a UUID.");
        var problem = controller.ProblemDetailsFactory.CreateValidationProblemDetails(controller.HttpContext, errors);
        return new ObjectResult(problem) { StatusCode = problem.Status, ContentTypes = { "application/problem+json" } };
    }
}
