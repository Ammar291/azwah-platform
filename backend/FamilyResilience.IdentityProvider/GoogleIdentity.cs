namespace FamilyResilience.IdentityProvider;

/// <summary>A Google account as the provider keeps it: Google's stable <c>sub</c> and the masked profile.</summary>
internal sealed record GoogleIdentity(string Subject, UpstreamProfile Profile)
{
    public const string SubjectPrefix = "google|";

    /// <summary>
    /// The staff account's prefix (D-149): a subject of its own, so the same person's family account (<c>google|</c>,
    /// from the phone) and staff account never share the roles the API records per subject.
    /// </summary>
    public const string StaffSubjectPrefix = "google-staff|";

    /// <summary>
    /// The persona the grant carries: a participant whose subject is the Google account's <c>sub</c> or, with
    /// <paramref name="staffRoles"/> (Google staff sign-in, D-149), a staff account with those roles.
    /// </summary>
    public Persona ToPersona(IReadOnlyList<string>? staffRoles = null) => staffRoles is { Count: > 0 }
        ? new("google_staff", StaffSubjectPrefix + Subject, staffRoles, "Google account (staff)")
        : new("google", SubjectPrefix + Subject, ["participant"], "Google account");
}
