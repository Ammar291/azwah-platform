using System.Diagnostics.Metrics;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Identity;

namespace FamilyResilience.Api.Observability;

/// <summary>
/// Security counters, exported with the other OpenTelemetry metrics and watched by <c>infra/monitoring/alert-rules.yml</c>
/// (D-90). Tags carry fixed codes only (reasons, policies, kinds, platform role codes), never subjects, IDs or request
/// data. One instance per host, from the host's <see cref="IMeterFactory"/>.
/// </summary>
public sealed class SecurityMetrics
{
    public const string MeterName = "FamilyResilience.Api.Security";

    private readonly Counter<long> _tokensRejected;
    private readonly Counter<long> _requestsRateLimited;
    private readonly Counter<long> _authorizationDenied;
    private readonly Counter<long> _roleChanges;
    private readonly Counter<long> _householdsErased;
    private readonly Counter<long> _erasureFailures;

    public SecurityMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MeterName);
        _tokensRejected = meter.CreateCounter<long>("azwah.auth.tokens_rejected", description: "Bearer tokens the API refused, by reason.");
        _requestsRateLimited = meter.CreateCounter<long>(
            "azwah.requests.rate_limited", description: "Requests refused by a rate limit, by the endpoint's policy.");
        _authorizationDenied = meter.CreateCounter<long>(
            "azwah.authorization.denied",
            description: "Signed-in requests refused: kind resource (answered 404) or role (answered 403).");
        _roleChanges = meter.CreateCounter<long>(
            "azwah.identity.role_changes",
            description: "Platform roles a subject gained or lost, by change and role; first_seen marks a subject's first request.");
        _householdsErased = meter.CreateCounter<long>(
            "azwah.retention.households_erased", description: "Withdrawn households the retention job erased.");
        _erasureFailures = meter.CreateCounter<long>(
            "azwah.retention.erasure_failures", description: "Erasures the retention job attempted that failed and were rolled back.");
    }

    public void TokenRejected(string reason) => _tokensRejected.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void RateLimited(string policy) =>
        _requestsRateLimited.Add(1, new KeyValuePair<string, object?>("endpoint_policy", policy));

    /// <summary><paramref name="kind"/>: <c>resource</c> or <c>role</c>.</summary>
    public void AuthorizationDenied(string kind) => _authorizationDenied.Add(1, new KeyValuePair<string, object?>("kind", kind));

    public void RolesChanged(ObservedRoleChange observed)
    {
        ArgumentNullException.ThrowIfNull(observed);
        var firstSeen = observed.FirstSeen ? "true" : "false";
        foreach (var (change, role) in observed.Change.Added.Select(role => ("added", role))
                     .Concat(observed.Change.Removed.Select(role => ("removed", role))))
        {
            _roleChanges.Add(
                1,
                new KeyValuePair<string, object?>("change", change),
                new KeyValuePair<string, object?>("role", role),
                new KeyValuePair<string, object?>("first_seen", firstSeen));
        }
    }

    public void RetentionRan(RetentionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.HouseholdsErased > 0)
        {
            _householdsErased.Add(report.HouseholdsErased);
        }

        if (report.Failures.Count > 0)
        {
            _erasureFailures.Add(report.Failures.Count);
        }
    }
}
