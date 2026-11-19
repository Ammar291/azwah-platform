using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Abstractions;

public static class RequestErrors
{
    public static readonly Error IdempotencyKeyReused =
        new("requests.idempotency_key_reused", "This Idempotency-Key was already used for a different request.");

    public static readonly Error HttpsRequired =
        new("requests.https_required", "This API accepts requests over HTTPS only.");

    public static readonly Error RateLimited =
        new("requests.rate_limited", "Too many requests. Wait for the time in Retry-After, then try again.");
}
