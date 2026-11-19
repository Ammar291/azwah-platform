using System.Text.RegularExpressions;
using FamilyResilience.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>One method on one route of the real API, with what its authorization metadata says.</summary>
internal sealed record ApiRoute(string Method, string Pattern, IReadOnlyList<string> Policies, bool Anonymous)
{
    public override string ToString() => $"{Method} /{Pattern}";

    public bool HasParameter(string name) => Pattern.Contains($"{{{name}", StringComparison.Ordinal);

    /// <summary>The route with every parameter filled: from <paramref name="values"/> when given, otherwise a new GUID.</summary>
    public string Url(IReadOnlyDictionary<string, string>? values = null) =>
        "/" + RouteInventory.Parameter().Replace(Pattern, match =>
        {
            var name = match.Groups["name"].Value;
            if (values is not null && values.TryGetValue(name, out var value))
            {
                return value;
            }

            return match.Groups["constraint"].Value switch
            {
                "guid" => Guid.NewGuid().ToString(),
                "int" => "1",
                _ => "unknown_code",
            };
        });

    public HttpRequestMessage Request(IReadOnlyDictionary<string, string>? values = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(Method), Url(values));
        if (Method is "POST" or "PUT" or "PATCH")
        {
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        }

        return request;
    }
}

/// <summary>Every route the API maps (test-only routes left out), so a sweep covers endpoints added later too.</summary>
internal static partial class RouteInventory
{
    /// <summary>Policies whose callers act for themselves or their household.</summary>
    public static readonly IReadOnlySet<string> ParticipantPolicies = new HashSet<string>(StringComparer.Ordinal)
    {
        Policies.Participant, Policies.HouseholdManager, Policies.AssignmentParticipant, Policies.SessionParticipant,
    };

    /// <summary>Policies that admit staff roles only.</summary>
    public static readonly IReadOnlySet<string> StaffPolicies = new HashSet<string>(StringComparer.Ordinal)
    {
        Policies.Researcher, Policies.ExportRequester, Policies.ProgramAdministrator, Policies.SystemAdministrator,
        Policies.SupportCoordinator, Policies.PartnerReader, Policies.AnalyticsReader,
    };

    public static IReadOnlyList<ApiRoute> Of(IServiceProvider services) =>
        services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => !(endpoint.RoutePattern.RawText ?? string.Empty).StartsWith("test-only", StringComparison.OrdinalIgnoreCase))
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => new ApiRoute(
                    method,
                    (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/'),
                    endpoint.Metadata.OfType<IAuthorizeData>().Select(data => data.Policy).OfType<string>().Distinct(StringComparer.Ordinal).ToList(),
                    endpoint.Metadata.OfType<IAllowAnonymous>().Any())))
            .OrderBy(route => route.Pattern, StringComparer.Ordinal)
            .ThenBy(route => route.Method, StringComparer.Ordinal)
            .ToList();

    [GeneratedRegex(@"\{(?<name>[A-Za-z]+)(:(?<constraint>[a-z]+))?\}")]
    internal static partial Regex Parameter();
}
