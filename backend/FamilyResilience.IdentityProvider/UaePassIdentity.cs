namespace FamilyResilience.IdentityProvider;

/// <summary>A UAE PASS staging account as the provider keeps it: the stable <c>uuid</c> and the masked profile.</summary>
internal sealed record UaePassIdentity(string Uuid, UpstreamProfile Profile)
{
    public const string SubjectPrefix = "uaepass-stg|";

    /// <summary>The persona the grant carries: a participant whose subject is the staging account's uuid.</summary>
    public Persona ToPersona() => new("uae_pass_staging", SubjectPrefix + Uuid, ["participant"], "UAE PASS staging account");
}
