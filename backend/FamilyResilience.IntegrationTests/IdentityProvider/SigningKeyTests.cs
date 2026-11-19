using System.Net;
using System.Security.Cryptography;
using FamilyResilience.IdentityProvider;

namespace FamilyResilience.IntegrationTests.IdentityProvider;

/// <summary>
/// The signing key: configured as a PEM (<c>IdentityProvider:SigningKey:Pem</c>) it is stable across restarts; without
/// one the development provider keeps a random key per process.
/// </summary>
public sealed class SigningKeyTests
{
    private static async Task<(string Kid, string Modulus)> ReadKeyAsync(IdentityProviderFactory factory)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/jwks", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var jwks = await IdentityProviderFlow.ReadJsonAsync(response, TestContext.Current.CancellationToken);
        var key = Assert.Single(jwks.GetProperty("keys").EnumerateArray().ToList());
        return (key.GetProperty("kid").GetString()!, key.GetProperty("n").GetString()!);
    }

    [Fact]
    public async Task A_configured_pem_key_gives_a_stable_kid_across_restarts()
    {
        var pem = IdentityProviderFactory.TestRsaPem();
        await using var first = new IdentityProviderFactory { SigningKeyPem = pem };
        await using var second = new IdentityProviderFactory { SigningKeyPem = pem };

        var one = await ReadKeyAsync(first);
        var two = await ReadKeyAsync(second);

        Assert.Equal(one, two);
        Assert.Equal(16, one.Kid.Length);
    }

    [Fact]
    public async Task Without_a_pem_the_key_is_random_per_process()
    {
        await using var first = new IdentityProviderFactory();
        await using var second = new IdentityProviderFactory();

        var one = await ReadKeyAsync(first);
        var two = await ReadKeyAsync(second);

        Assert.NotEqual(one.Kid, two.Kid);
        Assert.NotEqual(one.Modulus, two.Modulus);
    }

    [Fact]
    public void Problem_names_an_unparseable_or_short_key()
    {
        using var small = RSA.Create(1024);

        Assert.Equal("unparseable", SigningKey.Problem("not a key"));
        Assert.Equal("too_short", SigningKey.Problem(small.ExportPkcs8PrivateKeyPem()));
        Assert.Null(SigningKey.Problem(IdentityProviderFactory.TestRsaPem()));
    }
}
