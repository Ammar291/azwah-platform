using System.Diagnostics.Metrics;
using System.Globalization;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.Evaluation;
using FamilyResilience.Infrastructure.Assistant;
using Microsoft.Extensions.Logging.Abstractions;

// The live evaluation of the Azwah assistant and consultant (D-127). Synthetic data only; it needs an OpenAI key in
// OPENAI_API_KEY or the nearest .env above the working folder, and it costs money, so it runs only when asked:
//   dotnet run --project backend/FamilyResilience.Evaluation -- --suite all --out <report.md> [--min-pass 0.9]
var suite = Option("--suite") ?? "all";
var output = Option("--out") ?? Path.Combine(Path.GetTempPath(), $"azwah-evaluation-{DateTime.UtcNow:yyyyMMdd-HHmmss}.md");
var minimum = double.Parse(Option("--min-pass") ?? "0.85", CultureInfo.InvariantCulture);
var only = Option("--only");

var apiKey = EvaluationKey.Find() ?? throw new InvalidOperationException("Set OPENAI_API_KEY or put it in a .env file above this folder.");
var settings = OpenAiSettings.From(key => key == OpenAiSettings.ApiKeySetting ? apiKey : Environment.GetEnvironmentVariable(key.Replace(":", "__", StringComparison.Ordinal)));
var judgeSettings = settings with { ChatModel = Environment.GetEnvironmentVariable("AZWAH_JUDGE_MODEL") ?? "gpt-6.1-sol" };
using var http = new HttpClient(HappyEyeballs.CreateHandler()) { Timeout = TimeSpan.FromSeconds(60) };
using var meters = new EvaluationMeters();
var metrics = new AssistantMetrics(meters);
var tools = new EvaluationTools(metrics);
var model = new OpenAiAssistantModel(settings, http, new CodeLogger<OpenAiAssistantModel>());
var safety = new OpenAiModerationCheck(settings, http, NullLogger<OpenAiModerationCheck>.Instance);
var judge = new Judge(new OpenAiAssistantModel(judgeSettings, http, NullLogger<OpenAiAssistantModel>.Instance));
using var connector = new WebSocketRealtimeConnector();
var voice = new OpenAiLiveConsultantModel(settings, new TimingConnector(connector), safety, metrics, new CodeLogger<OpenAiLiveConsultantModel>());
var screen = new CrisisSignalScreen(CrisisSignalCatalog.Load());

var reports = new List<ScenarioReport>();
var timings = new Dictionary<string, List<double>>
{
    ["Chat reply"] = [],
    ["Call: first sound of the greeting"] = [],
    ["Call: first sound after the person stops"] = [],
};
using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    cancel.Cancel();
};

if (suite is "all" or "chat")
{
    var chat = new ChatEvaluation(new AssistantAgent(model, tools, safety, metrics), tools, judge, screen);
    foreach (var scenario in Scenarios.Chat().Where(scenario => only is null || scenario.Name.Contains(only, StringComparison.OrdinalIgnoreCase)))
    {
        Console.WriteLine($"chat: {scenario.Name}");
        var report = await chat.RunAsync(scenario, cancel.Token);
        timings["Chat reply"].AddRange(report.Lines.Where(line => line.Latency is not null).Select(line => line.Latency!.Value.TotalSeconds));
        Print(report);
        reports.Add(report);
    }
}

if (suite is "all" or "voice")
{
    // Calls run as the API runs them (D-141): the earlier conversation screened, the call carried through filter stops.
    var calls = new VoiceEvaluation(
        voice,
        tools,
        new SpeechSynthesis(http, apiKey),
        screen,
        new ConsultantHistoryScreen(screen, safety, AssistantLimits.Default),
        new OpenAiCrisisReply(settings, http, new CodeLogger<OpenAiCrisisReply>()),
        metrics);
    foreach (var scenario in Scenarios.Voice().Where(scenario => only is null || scenario.Name.Contains(only, StringComparison.OrdinalIgnoreCase)))
    {
        Console.WriteLine($"voice: {scenario.Name}");
        try
        {
            var (report, greeting, answers) = await calls.RunAsync(scenario, timings, cancel.Token);
            if (greeting is { } first)
            {
                timings["Call: first sound of the greeting"].Add(first);
            }

            timings["Call: first sound after the person stops"].AddRange(answers);
            Print(report);
            reports.Add(report);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var failed = new ScenarioReport($"voice: {scenario.Name}");
            failed.Add("(call)", string.Empty, null).Check("the call ran", exception.GetType().Name);
            Print(failed);
            reports.Add(failed);
        }
    }
}

var markdown = EvaluationReport.Markdown(
    reports, timings, $"Models: chat {settings.ChatModel}, voice {settings.VoiceModel} delegating to {settings.DelegateModel}, supportive reply {settings.CrisisModel}, judge {judgeSettings.ChatModel}.");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
await File.WriteAllTextAsync(output, markdown);
var passRate = reports.Count == 0 ? 0 : reports.Count(report => report.Passed) / (double)reports.Count;
Console.WriteLine();
Console.WriteLine($"{reports.Count(report => report.Passed)} of {reports.Count} scenarios passed ({passRate:P0}); report: {Path.GetFullPath(output)}");
foreach (var (name, values) in timings.Where(timing => timing.Value.Count > 0))
{
    Console.WriteLine($"{name}: median {EvaluationReport.Percentile(values, 0.5):0.00} s, 90th percentile {EvaluationReport.Percentile(values, 0.9):0.00} s");
}

return passRate >= minimum ? 0 : 1;

string? Option(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static void Print(ScenarioReport report)
{
    Console.WriteLine($"  {(report.Passed ? "PASS" : "FAIL")}");
    foreach (var failure in report.Lines.SelectMany(line => line.Failures))
    {
        Console.WriteLine($"    - {failure}");
    }
}

/// <summary>Meters for the run; nothing is exported.</summary>
internal sealed class EvaluationMeters : IMeterFactory
{
    private readonly List<Meter> _meters = [];

    public Meter Create(MeterOptions options)
    {
        var meter = new Meter(options);
        _meters.Add(meter);
        return meter;
    }

    public void Dispose() => _meters.ForEach(meter => meter.Dispose());
}
