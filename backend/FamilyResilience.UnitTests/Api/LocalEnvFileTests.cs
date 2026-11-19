using FamilyResilience.Api.Hosting;
using FamilyResilience.Infrastructure.Assistant;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace FamilyResilience.UnitTests.Api;

/// <summary>The local .env's OpenAI key, read for Development only. Every key here is synthetic and every folder temporary.</summary>
public sealed class LocalEnvFileTests : IDisposable
{
    private const string Key = "sk-synthetic-test-key";

    private readonly string _root = Directory.CreateTempSubdirectory("azwah-env-").FullName;

    private string ContentRoot
    {
        get
        {
            var folder = Path.Combine(_root, "backend", "FamilyResilience.Api");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private Task WriteEnvAsync(string contents, string? folder = null) =>
        File.WriteAllTextAsync(Path.Combine(folder ?? _root, ".env"), contents, TestContext.Current.CancellationToken);

    private ConfigurationManager Apply(string environment, string? existing = null)
    {
        var configuration = new ConfigurationManager();
        if (existing is not null)
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { [OpenAiSettings.ApiKeySetting] = existing });
        }

        configuration.AddLocalOpenAiKey(new TestEnvironment(environment, ContentRoot));
        return configuration;
    }

    [Fact]
    public async Task In_development_the_key_comes_from_the_nearest_env_file_above_the_project()
    {
        await WriteEnvAsync($"POSTGRES_USER=azwah\n{LocalEnvFile.OpenAiKeyVariable}={Key}\n");

        Assert.Equal(Key, Apply(Environments.Development)[OpenAiSettings.ApiKeySetting]);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public async Task Anywhere_else_the_file_is_not_read(string environment)
    {
        await WriteEnvAsync($"{LocalEnvFile.OpenAiKeyVariable}={Key}\n");

        Assert.Null(Apply(environment)[OpenAiSettings.ApiKeySetting]);
    }

    [Fact]
    public async Task A_key_already_set_elsewhere_wins()
    {
        await WriteEnvAsync($"{LocalEnvFile.OpenAiKeyVariable}={Key}\n");

        Assert.Equal("sk-from-user-secrets", Apply(Environments.Development, existing: "sk-from-user-secrets")[OpenAiSettings.ApiKeySetting]);
    }

    [Theory]
    [InlineData("OPENAI_API_KEY=")]
    [InlineData("OPENAI_API_KEY=   ")]
    [InlineData("# OPENAI_API_KEY=sk-commented-out")]
    [InlineData("NOT_OPENAI_API_KEY=sk-another-variable")]
    [InlineData("")]
    public async Task An_empty_commented_or_absent_key_leaves_the_assistant_and_the_report_off(string line)
    {
        await WriteEnvAsync($"POSTGRES_USER=azwah\n{line}\n");

        Assert.Null(Apply(Environments.Development)[OpenAiSettings.ApiKeySetting]);
    }

    [Fact]
    public void Without_a_file_it_stays_off() =>
        Assert.Null(Apply(Environments.Development)[OpenAiSettings.ApiKeySetting]);

    [Theory]
    [InlineData("OPENAI_API_KEY=sk-abc", "sk-abc")]
    [InlineData("  OPENAI_API_KEY = sk-abc  ", "sk-abc")]
    [InlineData("OPENAI_API_KEY=\"sk-abc\"", "sk-abc")]
    [InlineData("OPENAI_API_KEY='sk-abc'", "sk-abc")]
    [InlineData("OPENAI_API_KEY=sk-abc\r", "sk-abc")]
    [InlineData("OPENAI_API_KEY=sk=with=equals", "sk=with=equals")]
    public async Task A_pasted_key_is_read_however_it_was_written(string line, string expected)
    {
        await WriteEnvAsync($"{line}\n");

        Assert.Equal(expected, LocalEnvFile.Read(ContentRoot, LocalEnvFile.OpenAiKeyVariable));
    }

    [Fact]
    public async Task The_nearest_file_decides_even_when_it_has_no_key()
    {
        var contentRoot = ContentRoot;
        await WriteEnvAsync($"{LocalEnvFile.OpenAiKeyVariable}={Key}\n");
        await WriteEnvAsync("POSTGRES_USER=azwah\n", Path.Combine(_root, "backend"));

        Assert.Null(LocalEnvFile.Read(contentRoot, LocalEnvFile.OpenAiKeyVariable));
    }

    private sealed class TestEnvironment(string name, string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = contentRoot;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
