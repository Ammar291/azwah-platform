namespace FamilyResilience.Application.Identity;

/// <summary>The platform's own apps, which record their sign-ins and sign-outs (<see cref="RecordSessionEvent"/>).</summary>
public static class AuthClients
{
    public const string Admin = "admin";
    public const string Mobile = "mobile";

    /// <summary>Audit resource type of a sign-in or sign-out record; the resource ID is the client code.</summary>
    public const string ResourceType = "auth_client";

    public static IReadOnlyList<string> All { get; } = [Admin, Mobile];
}
