namespace FamilyResilience.IdentityProvider;

/// <summary>
/// Settings under "IdentityProvider:SigningKey". <see cref="Pem"/> is an unencrypted RSA private key (PKCS#8 or
/// PKCS#1 PEM) of at least 2048 bits, from a secret store, never a committed file. Required outside Development; in
/// Development a random key per process is used when it is absent.
/// </summary>
public sealed class SigningKeyOptions
{
    public string? Pem { get; set; }
}
