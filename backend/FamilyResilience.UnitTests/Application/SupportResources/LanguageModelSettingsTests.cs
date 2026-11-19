using FamilyResilience.Application.SupportResources;

namespace FamilyResilience.UnitTests.Application.SupportResources;

public sealed class LanguageModelSettingsTests
{
    private const string Section = "SupportResources:LanguageModel";

    private static LanguageModelSettings From(Dictionary<string, string?> settings) =>
        LanguageModelSettings.From(key => settings.GetValueOrDefault(key));

    /// <summary>Switched on with an approval reference; everything else as given.</summary>
    private static LanguageModelSettings Enabled(string? key = null, string? value = null)
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{Section}:Enabled"] = "true",
            [$"{Section}:ApprovalReference"] = "TEST-ONLY",
        };
        if (key is not null)
        {
            settings[$"{Section}:{key}"] = value;
        }

        return From(settings);
    }

    [Fact]
    public void Defaults_keep_the_model_off()
    {
        var defaults = LanguageModelSettings.Default;

        Assert.Equal(new LanguageModelSettings(false, null, 5, 2, 12, 256), defaults);
        Assert.Equal(defaults, From([]));
        // The block in appsettings.json says the same thing.
        Assert.Equal(defaults, From(new()
        {
            [$"{Section}:Enabled"] = "false",
            [$"{Section}:ApprovalReference"] = "",
            [$"{Section}:TimeoutSeconds"] = "5",
            [$"{Section}:MaxAttempts"] = "2",
            [$"{Section}:MaxCandidates"] = "12",
            [$"{Section}:MaxOutputTokens"] = "256",
        }));
        Assert.Equal("SupportResources:LanguageModel", LanguageModelSettings.Section);
    }

    [Fact]
    public void A_disabled_model_has_no_problems_whatever_else_is_set()
    {
        var settings = From(new()
        {
            [$"{Section}:Enabled"] = "false",
            [$"{Section}:ApprovalReference"] = "",
            [$"{Section}:TimeoutSeconds"] = "0",
            [$"{Section}:MaxAttempts"] = "99",
            [$"{Section}:MaxCandidates"] = "-1",
            [$"{Section}:MaxOutputTokens"] = "lots",
        });

        Assert.Empty(settings.Problems(clientRegistered: false));
    }

    [Theory]
    [InlineData("ApprovalReference", "", true, "approval_reference_missing")]
    [InlineData("ApprovalReference", "   ", true, "approval_reference_missing")]
    [InlineData(null, null, false, "provider_missing")]
    [InlineData("TimeoutSeconds", "0", true, "timeout_out_of_range")]
    [InlineData("TimeoutSeconds", "31", true, "timeout_out_of_range")]
    [InlineData("MaxAttempts", "0", true, "attempts_out_of_range")]
    [InlineData("MaxAttempts", "4", true, "attempts_out_of_range")]
    [InlineData("MaxCandidates", "0", true, "candidates_out_of_range")]
    [InlineData("MaxCandidates", "21", true, "candidates_out_of_range")]
    [InlineData("MaxOutputTokens", "63", true, "output_tokens_out_of_range")]
    [InlineData("MaxOutputTokens", "1025", true, "output_tokens_out_of_range")]
    public void An_enabled_model_names_each_problem(string? key, string? value, bool clientRegistered, string problem) =>
        Assert.Equal([problem], Enabled(key, value).Problems(clientRegistered));

    [Fact]
    public void An_approval_reference_longer_than_a_hundred_characters_is_missing()
    {
        Assert.Empty(Enabled("ApprovalReference", new string('D', 100)).Problems(clientRegistered: true));
        Assert.Equal(
            ["approval_reference_missing"], Enabled("ApprovalReference", new string('D', 101)).Problems(clientRegistered: true));
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("on")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unparsable_flag_means_off(string? flag)
    {
        var settings = From(new() { [$"{Section}:Enabled"] = flag, [$"{Section}:ApprovalReference"] = "TEST-ONLY" });

        Assert.False(settings.Enabled);
        Assert.Empty(settings.Problems(clientRegistered: false));
    }

    [Theory]
    [InlineData("TimeoutSeconds", "five", "timeout_out_of_range")]
    [InlineData("MaxAttempts", "2.5", "attempts_out_of_range")]
    [InlineData("MaxCandidates", "", "candidates_out_of_range")]
    [InlineData("MaxOutputTokens", "256 tokens", "output_tokens_out_of_range")]
    public void An_unparsable_number_is_out_of_range(string key, string value, string problem)
    {
        var settings = Enabled(key, value);

        Assert.Equal([problem], settings.Problems(clientRegistered: true));
        Assert.Equal(0, key switch
        {
            "TimeoutSeconds" => settings.TimeoutSeconds,
            "MaxAttempts" => settings.MaxAttempts,
            "MaxCandidates" => settings.MaxCandidates,
            _ => settings.MaxOutputTokens,
        });
    }
}
