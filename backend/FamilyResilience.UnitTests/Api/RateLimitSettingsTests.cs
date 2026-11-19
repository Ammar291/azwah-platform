using FamilyResilience.Api.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace FamilyResilience.UnitTests.Api;

public sealed class RateLimitSettingsTests
{
    private static RateLimitSettings From(Dictionary<string, string?> settings) =>
        RateLimitSettings.From(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

    [Fact]
    public void Rate_limiting_is_on_by_default_with_the_documented_limits()
    {
        var settings = From([]);

        Assert.True(settings.Enabled);
        Assert.Equal(new RateLimitWindow(300, 60), settings.SignedIn);
        Assert.Equal(new RateLimitWindow(60, 60), settings.Anonymous);
        Assert.Equal(new RateLimitWindow(30, 60), settings.Policies[RateLimitPolicies.ParticipantWrites]);
        Assert.Equal(new RateLimitWindow(120, 60), settings.Policies[RateLimitPolicies.Answers]);
        Assert.Equal(new RateLimitWindow(10, 60), settings.Policies[RateLimitPolicies.Identity]);
        Assert.Equal(new RateLimitWindow(60, 60), settings.Policies[RateLimitPolicies.StaffReads]);
        Assert.Equal(new RateLimitWindow(30, 60), settings.Policies[RateLimitPolicies.StaffWrites]);
        Assert.Equal(new RateLimitWindow(10, 3600), settings.Policies[RateLimitPolicies.Exports]);
        Assert.Equal(new RateLimitWindow(30, 3600), settings.Policies[RateLimitPolicies.ExportDownloads]);
        Assert.Equal(new RateLimitWindow(30, 60), settings.Policies[RateLimitPolicies.SupportSearches]);
    }

    [Fact]
    public void Only_an_explicit_false_turns_it_off()
    {
        Assert.False(From(new() { ["RateLimiting:Enabled"] = "false" }).Enabled);
        Assert.True(From(new() { ["RateLimiting:Enabled"] = "no" }).Enabled);
    }

    [Fact]
    public void Configured_limits_replace_the_defaults_and_stay_at_least_one()
    {
        var settings = From(new()
        {
            ["RateLimiting:Policies:identity:PermitLimit"] = "2",
            ["RateLimiting:Policies:identity:WindowSeconds"] = "5",
            ["RateLimiting:Policies:exports:PermitLimit"] = "0",
            ["RateLimiting:Global:AnonymousPermitLimit"] = "-3",
            ["RateLimiting:Global:SignedInPermitLimit"] = "lots",
        });

        Assert.Equal(new RateLimitWindow(2, 5), settings.Policies[RateLimitPolicies.Identity]);
        Assert.Equal(1, settings.Policies[RateLimitPolicies.Exports].PermitLimit);
        Assert.Equal(1, settings.Anonymous.PermitLimit);
        Assert.Equal(300, settings.SignedIn.PermitLimit);
    }
}
