namespace FamilyResilience.IdentityProvider;

/// <summary>
/// What an upstream sign-in released about the person, already minimised: identity numbers, phone numbers and e-mail
/// addresses are masked before this record is made, so the full values never reach a grant, a token or a log. Every
/// field is optional. Kept in memory with the grant only, for the ID tokens of that sign-in.
/// </summary>
internal sealed record UpstreamProfile(
    string Source,
    string? Name,
    string? GivenName,
    string? FamilyName,
    string? Gender,
    string? NameAr,
    string? GivenNameAr,
    string? FamilyNameAr,
    string? NationalityEn,
    string? NationalityAr,
    string? UserType,
    string? EmiratesIdMasked,
    string? MobileMasked,
    string? EmailMasked)
{
    public const string UaePassStagingSource = "uae_pass_staging";

    public const string GoogleSource = "google";
}
