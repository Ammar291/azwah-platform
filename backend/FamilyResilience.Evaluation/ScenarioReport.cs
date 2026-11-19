using System.Globalization;
using System.Text;

namespace FamilyResilience.Evaluation;

/// <summary>One scenario's turns, what was said back, how long it took and which checks held.</summary>
internal sealed class ScenarioReport(string name)
{
    public string Name { get; } = name;

    public List<ReportLine> Lines { get; } = [];

    public bool Passed => Lines.All(line => line.Failures.Count == 0);

    public ReportLine Add(string said, string reply, TimeSpan? latency)
    {
        var line = new ReportLine(said, reply, latency);
        Lines.Add(line);
        return line;
    }
}

internal sealed class ReportLine(string said, string reply, TimeSpan? latency)
{
    public string Said { get; } = said;

    public string Reply { get; } = reply;

    public TimeSpan? Latency { get; } = latency;

    public List<string> Passes { get; } = [];

    public List<string> Failures { get; } = [];

    public List<string> Notes { get; } = [];

    public void Check(string name, string? failure)
    {
        if (failure is null)
        {
            Passes.Add(name);
        }
        else
        {
            Failures.Add($"{name}: {failure}");
        }
    }
}

/// <summary>The whole run as Markdown: a summary, latency percentiles, then every scenario turn by turn.</summary>
internal static class EvaluationReport
{
    public static string Markdown(IReadOnlyList<ScenarioReport> scenarios, IReadOnlyDictionary<string, List<double>> timings, string models)
    {
        var builder = new StringBuilder();
        var checks = scenarios.SelectMany(scenario => scenario.Lines).Sum(line => line.Passes.Count + line.Failures.Count);
        var failed = scenarios.SelectMany(scenario => scenario.Lines).Sum(line => line.Failures.Count);
        builder.AppendLine(CultureInfo.InvariantCulture, $"# Azwah assistant and consultant evaluation ({DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC)");
        builder.AppendLine();
        builder.AppendLine("Synthetic families, scripted conversations and synthetic speech only (D-127). " + models);
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Scenarios passed: {scenarios.Count(scenario => scenario.Passed)} of {scenarios.Count}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Checks passed: {checks - failed} of {checks}");
        foreach (var (name, values) in timings.Where(timing => timing.Value.Count > 0))
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"- {name}: median {Percentile(values, 0.5):0.00} s, 90th percentile {Percentile(values, 0.9):0.00} s, worst {values.Max():0.00} s ({values.Count} samples)");
        }

        foreach (var scenario in scenarios)
        {
            builder.AppendLine();
            builder.AppendLine(CultureInfo.InvariantCulture, $"## {(scenario.Passed ? "PASS" : "FAIL")} {scenario.Name}");
            foreach (var line in scenario.Lines)
            {
                builder.AppendLine();
                builder.AppendLine(CultureInfo.InvariantCulture, $"- **Person:** {Flat(line.Said)}");
                builder.AppendLine(CultureInfo.InvariantCulture, $"- **Reply** ({(line.Latency is { } latency ? $"{latency.TotalSeconds:0.00} s" : "n/a")}): {Flat(line.Reply)}");
                foreach (var note in line.Notes)
                {
                    builder.AppendLine(CultureInfo.InvariantCulture, $"- {note}");
                }

                foreach (var failure in line.Failures)
                {
                    builder.AppendLine(CultureInfo.InvariantCulture, $"- FAILED {failure}");
                }

                if (line.Passes.Count > 0)
                {
                    builder.AppendLine(CultureInfo.InvariantCulture, $"- held: {string.Join("; ", line.Passes)}");
                }
            }
        }

        return builder.ToString();
    }

    public static double Percentile(List<double> values, double fraction)
    {
        var sorted = values.Order().ToList();
        var index = Math.Clamp((int)Math.Ceiling(fraction * sorted.Count) - 1, 0, sorted.Count - 1);
        return sorted[index];
    }

    private static string Flat(string text) => text.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", " / ", StringComparison.Ordinal);
}
