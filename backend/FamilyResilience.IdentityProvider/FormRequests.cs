using System.Net.Http.Headers;

namespace FamilyResilience.IdentityProvider;

/// <summary>Reads application/x-www-form-urlencoded bodies, the only body format OAuth endpoints accept.</summary>
internal static class FormRequests
{
    public const string MediaType = "application/x-www-form-urlencoded";

    /// <summary>The form, or null when the body is not a well-formed URL-encoded form.</summary>
    public static async Task<IFormCollection?> ReadAsync(HttpRequest request)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType)
            || !string.Equals(contentType.MediaType, MediaType, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return await request.ReadFormAsync(request.HttpContext.RequestAborted);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    public static bool HasRepeatedFields(IFormCollection form) => form.Any(field => field.Value.Count > 1);
}
