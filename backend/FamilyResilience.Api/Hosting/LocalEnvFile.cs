using FamilyResilience.Infrastructure.Assistant;

namespace FamilyResilience.Api.Hosting;

/// <summary>
/// Lets a developer keep the OpenAI key in the repository's git-ignored <c>.env</c> when running the API with
/// <c>dotnet run</c>, which does not read that file (Docker Compose passes the key on by itself). Development only, and
/// only this one variable: every other setting still comes from user secrets. A key already set elsewhere wins, and an
/// empty value leaves the assistant, the consultant and the score report switched off (D-112, D-119).
/// </summary>
public static class LocalEnvFile
{
    public const string OpenAiKeyVariable = "OPENAI_API_KEY";

    private const string FileName = ".env";

    public static void AddLocalOpenAiKey(this ConfigurationManager configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        if (!environment.IsDevelopment() || !string.IsNullOrWhiteSpace(configuration[OpenAiSettings.ApiKeySetting]))
        {
            return;
        }

        if (Read(environment.ContentRootPath, OpenAiKeyVariable) is { } key)
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { [OpenAiSettings.ApiKeySetting] = key });
        }
    }

    /// <summary>
    /// The variable's value in the nearest <c>.env</c> at or above <paramref name="startDirectory"/>, or null when it is
    /// empty, commented out or absent. Walking up finds the repository root from the project and from a worktree inside it.
    /// </summary>
    public static string? Read(string startDirectory, string variable)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, FileName);
            if (File.Exists(path))
            {
                return Find(File.ReadLines(path), variable);
            }
        }

        return null;
    }

    private static string? Find(IEnumerable<string> lines, string variable)
    {
        foreach (var line in lines)
        {
            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0 && line[..equals].Trim() == variable)
            {
                var value = line[(equals + 1)..].Trim().Trim('"', '\'').Trim();
                return value.Length == 0 ? null : value;
            }
        }

        return null;
    }
}
