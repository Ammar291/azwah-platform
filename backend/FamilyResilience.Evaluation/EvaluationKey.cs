namespace FamilyResilience.Evaluation;

/// <summary>
/// The OpenAI key for an evaluation run: <c>OPENAI_API_KEY</c> from the environment, else the same variable in the
/// nearest git-ignored <c>.env</c> above the working folder (as the API reads it in Development). Never printed.
/// </summary>
internal static class EvaluationKey
{
    private const string Variable = "OPENAI_API_KEY";

    public static string? Find()
    {
        if (Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } fromEnvironment)
        {
            return fromEnvironment.Trim();
        }

        for (var folder = new DirectoryInfo(Directory.GetCurrentDirectory()); folder is not null; folder = folder.Parent)
        {
            var file = Path.Combine(folder.FullName, ".env");
            if (!File.Exists(file))
            {
                continue;
            }

            foreach (var line in File.ReadLines(file))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith(Variable + "=", StringComparison.Ordinal))
                {
                    var value = trimmed[(Variable.Length + 1)..].Trim().Trim('"', '\'');
                    return value.Length > 0 ? value : null;
                }
            }
        }

        return null;
    }
}
