using System.Collections.Concurrent;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.IntegrationTests.Assistant;

/// <summary>A configured report model for test hosts: records every request and answers from the test. Nothing leaves it.</summary>
internal sealed class RecordingScoreReportModel(string behaviour = "usable") : IScoreReportModel
{
    public const string Name = "synthetic-model";

    public static readonly ScoreReportWriting Usable = new(
        "zqxsyntheticsummarymarker",
        [new ScoreReportPoint("A synthetic strength", "Why it counts.")],
        [new ScoreReportPoint("A synthetic weakness", "Why it counts.")],
        [new ScoreReportImprovement("A synthetic area", "Why it counts.", ["A synthetic step."])]);

    public static readonly ScoreReportPartWriting UsablePart = new(
        [new ScoreReportPoint("zqxsyntheticpartmarker", "Why it counts.")],
        [new ScoreReportPoint("A synthetic part weakness", "Why it counts.")],
        [new ScoreReportImprovement("A synthetic area", "Why it counts.", ["A synthetic step."])]);

    public static readonly ScoreReportSummaryWriting UsableSummary = new("zqxsyntheticclosingmarker");

    /// <summary>Nine words: two of them run past every budget but the summary's, three past that one too (D-145).</summary>
    public const string WordySentence = "Your family shares meals and talks about the day.";

    /// <summary>Six sentences.</summary>
    public static readonly string WordyText = string.Join(' ', Enumerable.Repeat(WordySentence, 6));

    private static readonly ScoreReportPoint WordyPoint = new("A synthetic point", WordyText);

    private static readonly ScoreReportImprovement WordyImprovement =
        new("A synthetic area", WordyText, [WordySentence, WordySentence, WordySentence, WordySentence]);

    private readonly ConcurrentQueue<ScoreReportFacts> _requests = new();
    private readonly ConcurrentQueue<ScoreReportPartFacts> _partRequests = new();
    private readonly ConcurrentQueue<ScoreReportSummaryFacts> _summaryRequests = new();

    public bool IsConfigured => true;

    public string ModelName => Name;

    public IReadOnlyList<ScoreReportFacts> Requests => [.. _requests];

    public IReadOnlyList<ScoreReportPartFacts> PartRequests => [.. _partRequests];

    public IReadOnlyList<ScoreReportSummaryFacts> SummaryRequests => [.. _summaryRequests];

    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The section codes a whole report's improvements claim, one improvement for each; empty keeps <see cref="Usable"/>.</summary>
    public IReadOnlyList<string> WholeReportCodes { get; init; } = [];

    /// <summary>The services a part's improvement picks; empty keeps <see cref="UsablePart"/>, which picks none.</summary>
    public IReadOnlyList<string> PartSupportCodes { get; init; } = [];

    public async Task<Result<ScoreReportWriting>> WriteAsync(ScoreReportFacts facts, CancellationToken cancellationToken)
    {
        _requests.Enqueue(facts);
        Started.TrySetResult();
        switch (behaviour)
        {
            case "failure":
                return Result.Failure<ScoreReportWriting>(AssistantErrors.ModelFailed);
            case "throws":
                throw new InvalidOperationException("Synthetic model fault.");
            case "empty":
                return Result.Success(new ScoreReportWriting(" ", [], [], []));
            case "too_long":
                return Result.Success(Usable with { Summary = new string('x', ScoreReportWriting.MaxSummaryLength + 1) });
            case "hangs":
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Unreachable.");
            case "wordy":
                return Result.Success(new ScoreReportWriting(
                    WordyText,
                    [WordyPoint, WordyPoint, WordyPoint],
                    [WordyPoint, WordyPoint, WordyPoint],
                    WholeReportCodes.Select(code => WordyImprovement with { SectionCode = code }).ToList()));
            default:
                return Result.Success(WholeReportCodes.Count == 0
                    ? Usable
                    : Usable with { Improvements = WholeReportCodes.Select(code => Usable.Improvements[0] with { SectionCode = code }).ToList() });
        }
    }

    public Task<Result<ScoreReportPartWriting>> WritePartAsync(ScoreReportPartFacts facts, CancellationToken cancellationToken)
    {
        _partRequests.Enqueue(facts);
        return behaviour switch
        {
            "failure" => Task.FromResult(Result.Failure<ScoreReportPartWriting>(AssistantErrors.ModelFailed)),
            "throws" => throw new InvalidOperationException("Synthetic model fault."),
            "empty" => Task.FromResult(Result.Success(new ScoreReportPartWriting([], [], []))),
            "too_many" => Task.FromResult(Result.Success(UsablePart with
            {
                Weaknesses = [new("First", "D"), new("Second", "D"), new("Third", "D")],
            })),
            "wordy" => Task.FromResult(Result.Success(new ScoreReportPartWriting([WordyPoint, WordyPoint], [WordyPoint], [WordyImprovement, WordyImprovement]))),
            _ => Task.FromResult(Result.Success(PartSupportCodes.Count == 0
                ? UsablePart
                : UsablePart with { Improvements = [UsablePart.Improvements[0] with { SupportCodes = PartSupportCodes }] })),
        };
    }

    public Task<Result<ScoreReportSummaryWriting>> WriteSummaryAsync(ScoreReportSummaryFacts facts, CancellationToken cancellationToken)
    {
        _summaryRequests.Enqueue(facts);
        return behaviour switch
        {
            "failure" => Task.FromResult(Result.Failure<ScoreReportSummaryWriting>(AssistantErrors.ModelFailed)),
            "throws" => throw new InvalidOperationException("Synthetic model fault."),
            "empty" => Task.FromResult(Result.Success(new ScoreReportSummaryWriting(" "))),
            "wordy" => Task.FromResult(Result.Success(new ScoreReportSummaryWriting(WordyText))),
            _ => Task.FromResult(Result.Success(UsableSummary)),
        };
    }
}
