using FamilyResilience.Application.Assistant;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Domain.Safeguarding;

namespace FamilyResilience.UnitTests.Application.Assistant;

/// <summary>Earlier lines held back from a call because they would trip the provider's filter (D-141). Synthetic text only.</summary>
public sealed class ConsultantHistoryScreenTests
{
    private static readonly AssistantLimits Limits = AssistantLimits.Default with { ConsultantHistoryCheckWait = TimeSpan.FromMilliseconds(200) };

    private static ConsultantHistoryScreen Create(IAssistantSafetyCheck safety, AssistantLimits? limits = null) =>
        new(new CrisisSignalScreen(CrisisSignalCatalog.Load()), safety, limits ?? Limits);

    private static readonly AssistantMessage Ordinary = new(AssistantMessageRole.User, "My kids argue about the console");

    private static readonly AssistantMessage Phrase = new(AssistantMessageRole.User, "I want to end my life", AssistantMessageChannel.Voice);

    private static readonly AssistantMessage Reply = new(AssistantMessageRole.Assistant, "If you have thoughts of suicide, press SOS", AssistantMessageChannel.Voice);

    [Fact]
    public async Task Lines_the_phrase_screen_matches_are_held_back_whoever_said_them_without_a_model()
    {
        var history = await Create(new NoAssistantSafetyCheck()).ScreenAsync([Ordinary, Phrase, Reply], TestContext.Current.CancellationToken);

        Assert.Equal([Ordinary], history.Kept);
        Assert.True(history.Withheld);
    }

    [Fact]
    public async Task Lines_the_safety_check_flags_are_held_back_and_the_rest_are_checked_together()
    {
        var safety = new VerdictsSafetyCheck(text => text.Contains("zqx", StringComparison.Ordinal) ? AssistantSafetyVerdict.DangerToSelf : AssistantSafetyVerdict.Clear);
        var flagged = new AssistantMessage(AssistantMessageRole.User, "zqx words the phrase list does not hold");

        var history = await Create(safety).ScreenAsync([Ordinary, Phrase, flagged], TestContext.Current.CancellationToken);

        Assert.Equal([Ordinary], history.Kept);
        Assert.True(history.Withheld);
        Assert.Equal([[Ordinary.Text, flagged.Text]], safety.Batches);
    }

    [Fact]
    public async Task A_conversation_with_nothing_to_hold_back_is_kept_whole()
    {
        var history = await Create(new FixedSafetyCheck(AssistantSafetyVerdict.Clear)).ScreenAsync([Ordinary], TestContext.Current.CancellationToken);

        Assert.Equal([Ordinary], history.Kept);
        Assert.False(history.Withheld);
    }

    [Fact]
    public async Task A_check_that_does_not_answer_in_time_holds_the_start_up_for_its_wait_only()
    {
        var safety = new FixedSafetyCheck(AssistantSafetyVerdict.DangerToSelf, TimeSpan.FromSeconds(30));
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var history = await Create(safety).ScreenAsync([Ordinary, Phrase], TestContext.Current.CancellationToken);

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Equal([Ordinary], history.Kept);
    }

    [Fact]
    public async Task An_empty_conversation_is_not_checked()
    {
        var safety = new FixedSafetyCheck(AssistantSafetyVerdict.Clear);

        var history = await Create(safety).ScreenAsync([], TestContext.Current.CancellationToken);

        Assert.Empty(history.Kept);
        Assert.False(history.Withheld);
        Assert.Empty(safety.Checked);
    }

    /// <summary>A check that answers each text from a function and records each batch it was given.</summary>
    private sealed class VerdictsSafetyCheck(Func<string, AssistantSafetyVerdict> verdict) : IAssistantSafetyCheck
    {
        public List<IReadOnlyList<string>> Batches { get; } = [];

        public bool IsConfigured => true;

        public Task<AssistantSafetyVerdict> CheckAsync(string text, CancellationToken cancellationToken) => Task.FromResult(verdict(text));

        public Task<IReadOnlyList<AssistantSafetyVerdict>> CheckManyAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
        {
            Batches.Add(texts);
            return Task.FromResult<IReadOnlyList<AssistantSafetyVerdict>>(texts.Select(verdict).ToList());
        }
    }
}
