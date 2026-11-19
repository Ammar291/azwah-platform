using System.Net;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Audit;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.IntegrationTests.Security;

/// <summary>
/// Sweeps every route the API maps, so an endpoint added later is covered without anyone remembering to add a test:
/// each one names a policy (or is one of the listed exceptions), refuses anonymous callers, and refuses the wrong roles.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DenyByDefaultTests(ApiFactory factory)
{
    /// <summary>The only routes anyone may call without signing in.</summary>
    private static readonly string[] Anonymous = ["health/live", "health/ready"];

    /// <summary>Routes about the caller themselves: any signed-in subject, through the fallback policy.</summary>
    private static readonly string[] AnySignedIn = ["api/v1/me", "api/v1/me/permissions", "api/v1/me/sign-ins", "api/v1/me/sign-outs"];

    private static readonly string[] StaffRoles =
        [Roles.Researcher, Roles.ProgramAdministrator, Roles.SystemAdministrator, Roles.SupportCoordinator];

    private IReadOnlyList<ApiRoute> Routes => RouteInventory.Of(factory.Services);

    [Fact]
    public void The_inventory_sees_the_api()
    {
        Assert.Contains(Routes, route => route.Pattern == "api/v1/households/{householdId:guid}" && route.Method == "GET");
        Assert.DoesNotContain(Routes, route => route.Pattern.StartsWith("test-only", StringComparison.Ordinal));
        Assert.True(Routes.Count > 50);
    }

    [Fact]
    public void Every_endpoint_names_a_policy_or_is_a_listed_exception()
    {
        var unexplained = Routes
            .Where(route => route.Anonymous ? !Anonymous.Contains(route.Pattern) : route.Policies.Count == 0 && !AnySignedIn.Contains(route.Pattern))
            .Select(route => route.ToString())
            .ToList();

        Assert.True(unexplained.Count == 0, $"Give these endpoints a named policy, or list them here with a reason: {string.Join(", ", unexplained)}");
    }

    [Fact]
    public async Task Every_protected_endpoint_answers_401_to_an_anonymous_caller()
    {
        using var client = factory.As(null);
        var wrong = new List<string>();

        foreach (var route in Routes.Where(route => !route.Anonymous))
        {
            using var request = route.Request();
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                wrong.Add($"{route} answered {(int)response.StatusCode}");
            }
        }

        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }

    [Fact]
    public async Task Every_staff_endpoint_answers_403_to_a_participant_and_audits_the_refusal()
    {
        var subject = ApiClients.NewSubject("participant");
        using var client = factory.AsParticipant(subject);
        var staffRoutes = Routes.Where(route => route.Policies.Count > 0 && route.Policies.All(RouteInventory.StaffPolicies.Contains)).ToList();
        var wrong = new List<string>();

        foreach (var route in staffRoutes)
        {
            using var request = route.Request();
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            if (response.StatusCode != HttpStatusCode.Forbidden)
            {
                wrong.Add($"{route} answered {(int)response.StatusCode}");
            }
        }

        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
        Assert.True(staffRoutes.Count > 25);
        var denials = await factory.QueryAsync(db => db.AuditEvents.AsNoTracking()
            .Where(audit => audit.ActorId == subject && audit.Action == "authorization.role.denied")
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(staffRoutes.Count, denials.Count);
        Assert.All(denials, denial => Assert.Equal(("endpoint", AuditOutcome.Denied), (denial.ResourceType, denial.Outcome)));
        Assert.Equal(
            staffRoutes.Select(route => route.Pattern).Distinct().Order(StringComparer.Ordinal),
            denials.Select(denial => denial.ResourceId!).Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Every_participant_endpoint_answers_403_to_each_staff_role()
    {
        var participantRoutes = Routes.Where(route => route.Policies.Count > 0 && route.Policies.All(RouteInventory.ParticipantPolicies.Contains)).ToList();
        var wrong = new List<string>();

        foreach (var role in StaffRoles)
        {
            using var client = factory.As(ApiClients.NewSubject(role), role);
            foreach (var route in participantRoutes)
            {
                using var request = route.Request();
                using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
                if (response.StatusCode != HttpStatusCode.Forbidden)
                {
                    wrong.Add($"{role}: {route} answered {(int)response.StatusCode}");
                }
            }
        }

        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
        Assert.True(participantRoutes.Count > 15);
    }

    [Fact]
    public async Task A_signed_in_caller_without_any_role_reaches_only_their_own_identity()
    {
        using var client = factory.As(ApiClients.NewSubject("no-role"));
        var reached = new List<string>();

        foreach (var route in Routes.Where(route => !route.Anonymous && !AnySignedIn.Contains(route.Pattern)))
        {
            using var request = route.Request();
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.NotFound))
            {
                reached.Add($"{route} answered {(int)response.StatusCode}");
            }
        }

        Assert.True(reached.Count == 0, string.Join("; ", reached));
    }
}
