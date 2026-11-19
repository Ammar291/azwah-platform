using FamilyResilience.Application.Identity;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FamilyResilience.IntegrationTests.Support;

internal static class ApiClients
{
    public const string SystemAdministratorSubject = "system-administrator-subject";

    /// <summary>A client of any test host (a shared one, or one derived from it) signed in through the test scheme; no subject means anonymous.</summary>
    public static HttpClient As(this WebApplicationFactory<Program> factory, string? subject, params string[] roles)
    {
        var client = factory.CreateClient();
        if (subject is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, subject);
        }

        if (roles.Length > 0)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(',', roles));
        }

        return client;
    }

    public static HttpClient AsSystemAdministrator(this ApiFactory factory) =>
        factory.As(SystemAdministratorSubject, Roles.SystemAdministrator);

    /// <summary>A participant with a subject no other test uses, unless one is given.</summary>
    public static HttpClient AsParticipant(this ApiFactory factory, string? subject = null) =>
        factory.As(subject ?? NewSubject("participant"), Roles.Participant);

    public static HttpClient AsResearcher(this ApiFactory factory, string? subject = null) =>
        factory.As(subject ?? NewSubject("researcher"), Roles.Researcher);

    public static HttpClient AsProgramAdministrator(this ApiFactory factory, string? subject = null) =>
        factory.As(subject ?? NewSubject("program-administrator"), Roles.ProgramAdministrator);

    public static string NewSubject(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
}
