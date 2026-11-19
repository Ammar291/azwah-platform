using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using FamilyResilience.Domain.Safeguarding;
using Microsoft.Extensions.Logging;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// One live call across the model sessions it needs (D-141). OpenAI's own filter ends a GPT-Live session when someone
/// speaks of hurting themselves; the call must not end with it. When a session reports
/// <see cref="ConsultantContentStopped"/> after the person spoke in it, this emits the provider-filter flag and SOS (the
/// relay turns them into the safety notice and records the alert, as before), then <see cref="ConsultantInterrupted"/>
/// and an empty final consultant line (the app hides and never saves it, so the cut reply is neither played on nor kept),
/// opens a new session that listens and starts with <see cref="AssistantPrompt.CrisisResumeNote"/> (never the person's
/// words), and has it say a supportive reply written from what the person said (<see cref="IConsultantCrisisReply"/>,
/// bounded by <see cref="AssistantLimits.ConsultantCrisisReplyWait"/>; the fixed words otherwise). The call carries on at
/// most <see cref="AssistantLimits.ConsultantResumeLimit"/> times; the next stop has a last session say
/// <see cref="AssistantPrompt.CrisisClosingWords"/> and ends the call. A session stopped before the person said anything
/// in it was stopped over what it was started with, and a session carrying the call on may fail: either is reopened once
/// (once more after each carry-on) without the earlier conversation and the memory, and nothing is raised. After a
/// carry-on the reopened session keeps the note and listens, and says the fixed words if the support was never heard.
/// Any other fault ends the call as before. While no session listens, microphone audio is dropped, not held.
/// Nothing the person said is logged or kept: the latest line is held in memory only to write the reply.
/// </summary>
public sealed partial class ContinuingConsultantSession : IVoiceConsultantSession
{
    /// <summary>The longest reply said as written, in characters: about 100 words, well within a spoken instruction's 500 tokens.</summary>
    public const int MaxWords = 600;

    private static readonly TimeSpan RetireWait = TimeSpan.FromSeconds(5);

    /// <summary>How long a frame that failed waits for its session to report a filter stop before the failure counts.</summary>
    private static readonly TimeSpan SendFailureGrace = TimeSpan.FromSeconds(1);

    private readonly IVoiceConsultantModel _model;
    private readonly IConsultantCrisisReply _crisis;
    private readonly AssistantLimits _limits;
    private readonly AssistantMetrics _metrics;
    private readonly ILogger _logger;
    private readonly string _language;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<Task> _retiring = [];
    private readonly Lock _gate = new();

    /// <summary>What a session that carries the call on starts from: the call's first request, or a bare one after a recovery.</summary>
    private VoiceConsultantRequest _base;

    private IVoiceConsultantSession? _leg;
    private IVoiceConsultantSession? _listening;

    /// <summary>Completed, and replaced, whenever the session that hears the microphone changes.</summary>
    private TaskCompletionSource _switched = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _resuming;
    private int _resumes;
    private bool _recovered;
    private bool _heardConsultant;
    private bool _disposed;

    private ContinuingConsultantSession(
        IVoiceConsultantModel model,
        VoiceConsultantRequest request,
        IConsultantCrisisReply crisis,
        AssistantLimits limits,
        AssistantMetrics metrics,
        ILogger logger,
        IVoiceConsultantSession first)
    {
        _model = model;
        _base = request;
        _crisis = crisis;
        _limits = limits;
        _metrics = metrics;
        _logger = logger;
        _language = request.Language;
        _leg = first;
        _listening = first;
    }

    /// <summary>The call's first session opened with <paramref name="request"/>, or null when the model failed, threw or ran out of time.</summary>
    public static async Task<ContinuingConsultantSession?> OpenAsync(
        IVoiceConsultantModel model,
        VoiceConsultantRequest request,
        IConsultantCrisisReply crisis,
        AssistantLimits limits,
        AssistantMetrics metrics,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await OpenAsync(model, Task.FromResult(request), crisis, limits, metrics, logger, cancellationToken);
    }

    /// <summary>
    /// The call's first session, opened while <paramref name="preparing"/> is still being prepared (the adapter may
    /// connect meanwhile), or null when the model failed, threw, ran out of time, or the request could not be prepared.
    /// <see cref="AssistantLimits.ModelTimeout"/> covers the preparation too.
    /// </summary>
    public static async Task<ContinuingConsultantSession?> OpenAsync(
        IVoiceConsultantModel model,
        Task<VoiceConsultantRequest> preparing,
        IConsultantCrisisReply crisis,
        AssistantLimits limits,
        AssistantMetrics metrics,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(preparing);
        ArgumentNullException.ThrowIfNull(limits);
        var first = await OpenLegAsync(token => model.OpenAsync(preparing, token), limits.ModelTimeout, cancellationToken);

        // A session opened only once its request was ready, so the request is there.
        return first is null ? null : new ContinuingConsultantSession(model, await preparing, crisis, limits, metrics, logger, first);
    }

    /// <summary>
    /// A model session opened within <paramref name="limit"/>, or null when the model failed, threw or ran out of time; a
    /// session that opens after that is closed at once. Only the caller's cancellation propagates.
    /// </summary>
    internal static Task<IVoiceConsultantSession?> OpenLegAsync(
        IVoiceConsultantModel model, VoiceConsultantRequest request, TimeSpan limit, CancellationToken cancellationToken) =>
        OpenLegAsync(token => model.OpenAsync(request, token), limit, cancellationToken);

    private static async Task<IVoiceConsultantSession?> OpenLegAsync(
        Func<CancellationToken, Task<IVoiceConsultantSession>> open, TimeSpan limit, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limit);
        Task<IVoiceConsultantSession>? opening = null;
        try
        {
            opening = open(timeout.Token);

            // WaitAsync bounds the call even when an adapter ignores its token.
            return await opening.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CloseWhenOpened(opening);
            throw;
        }
        catch (Exception)
        {
            // Broad on purpose: a time-out, a provider fault or an adapter bug all end the open the same way. The
            // exception is dropped unlogged, because an adapter's message could quote the request.
            CloseWhenOpened(opening);
            return null;
        }
    }

    public async Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken cancellationToken)
    {
        IVoiceConsultantSession? leg;
        Task switched;
        lock (_gate)
        {
            leg = _listening;
            switched = _switched.Task;
        }

        if (leg is null)
        {
            // No session listens (one is being opened, or the closing words play): the frame is not heard.
            return;
        }

        try
        {
            await leg.SendAudioAsync(pcm, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // OpenAI drops its socket right after a filter stop, so a frame can fail before the session has read the
            // stop already on its way (measured 2026-11-10): the stop gets a moment to arrive before this counts as a
            // model fault. A frame lost to a stopping session is dropped, and the call carries on.
            if (!IsListening(leg))
            {
                return;
            }

            await Task.WhenAny(switched, Task.Delay(SendFailureGrace, cancellationToken));
            if (!IsListening(leg))
            {
                return;
            }

            throw;
        }
    }

    public Task SpeakAsync(string words, CancellationToken cancellationToken)
    {
        IVoiceConsultantSession? leg;
        lock (_gate)
        {
            leg = _listening;
        }

        return leg?.SpeakAsync(words, cancellationToken) ?? Task.CompletedTask;
    }

    public async IAsyncEnumerable<VoiceConsultantEvent> ReadEventsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = linked.Token;
        var leg = Current() ?? throw new ObjectDisposedException(nameof(ContinuingConsultantSession));
        while (true)
        {
            var heardPerson = false;
            var lineOpen = false;
            string? said = null;
            var stopped = false;
            Exception? fault = null;
            var carriesOn = _resumes > 0;
            var spoke = false;
            await using var events = leg.ReadEventsAsync(token).GetAsyncEnumerator(token);
            while (true)
            {
                var (more, failure) = await TryNextAsync(events, token);
                if (failure is not null)
                {
                    fault = failure;
                    break;
                }

                if (!more)
                {
                    break;
                }

                var item = events.Current;
                if (item is ConsultantContentStopped)
                {
                    stopped = true;
                    break;
                }

                switch (item)
                {
                    case ConsultantTranscript { Speaker: ConsultantSpeaker.User } line:
                        heardPerson = true;
                        said = line.Text;
                        break;
                    case ConsultantTranscript { Speaker: ConsultantSpeaker.Consultant } line:
                        lineOpen = !line.Final;
                        break;
                    case ConsultantAudio:
                        _heardConsultant = true;
                        spoke = true;
                        break;
                }

                yield return item;
            }

            if (fault is not null && (!carriesOn || _recovered))
            {
                // A model fault: the call ends with model_failed, as before (D-124).
                ExceptionDispatchInfo.Throw(fault);
            }

            if (fault is null && !stopped)
            {
                // The model ended the session itself: the call ends with it.
                yield break;
            }

            Listen(null);
            Retire(leg);
            if (!heardPerson || fault is not null)
            {
                // Nothing from the person reached this session, so what it was started with stopped it (the earlier
                // conversation, the memory, the note or Azwah's own words); or a session carrying the call on failed,
                // which happened twice in about ten on 2026-11-10 (its socket dropped, after errors or none). Nothing
                // new was said to the filter: nothing is raised.
                if (_recovered)
                {
                    LogStopped(_logger, "stopped_again");
                    throw new InvalidOperationException("The voice model stopped the call again before the person spoke.");
                }

                var reason = fault is null ? "seeded" : "fault";
                _recovered = true;
                _base = _base with { Memory = null, History = [], Note = carriesOn ? AssistantPrompt.CrisisResumeNote : null };

                // After a carry-on the new session listens, and says the fixed words if the support was never heard.
                var words = carriesOn && !spoke ? Task.FromResult(AssistantPrompt.CrisisFallbackWords(_language)) : null;
                var opening = carriesOn || _heardConsultant ? ConsultantOpening.Listen : ConsultantOpening.Greet;
                var reopening = Track(StartAsync(_base with { Opening = opening }, words, listen: true, token));
                _metrics.ConsultantResumed(reason);
                LogResumed(_logger, reason);
                if (lineOpen)
                {
                    yield return new ConsultantInterrupted();
                    yield return new ConsultantTranscript(ConsultantSpeaker.Consultant, string.Empty, Final: true);
                }

                leg = await reopening ?? throw Failed();
                continue;
            }

            // What the person said tripped the filter: the provider filter's signal, as before (D-136), then support.
            _metrics.SafetyFlagged("voice_filter");
            var closing = _resumes >= _limits.ConsultantResumeLimit;
            var next = closing ? CloseAsync(token) : ResumeAsync(said!, token);
            yield return new ConsultantSafetyFlag(SafetyConcern.SuicideOrSelfHarm, SafetyDetector.ProviderFilter);
            yield return new ConsultantAction(AssistantScreens.Sos);
            yield return new ConsultantInterrupted();
            if (lineOpen)
            {
                yield return new ConsultantTranscript(ConsultantSpeaker.Consultant, string.Empty, Final: true);
            }

            if (!closing)
            {
                leg = await next ?? throw Failed();
                continue;
            }

            if (await next is { } last)
            {
                await foreach (var item in ClosingEventsAsync(last, token))
                {
                    yield return item;
                }

                // The closing words are said: the last session closes with the call.
                Retire(last);
            }

            yield break;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? resuming;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            resuming = _resuming;
        }

        await _lifetime.CancelAsync();
        if (resuming is not null)
        {
            try
            {
                await resuming.WaitAsync(RetireWait);
            }
            catch (Exception)
            {
                // Opening was cancelled with the call; a session that opens late is closed by the open itself.
            }
        }

        IVoiceConsultantSession? leg;
        Task[] retiring;
        lock (_gate)
        {
            leg = _leg;
            _leg = null;
            retiring = [.. _retiring];
        }

        if (leg is not null)
        {
            await DisposeQuietlyAsync(leg);
        }

        try
        {
            await Task.WhenAll(retiring).WaitAsync(RetireWait);
        }
        catch (Exception)
        {
            // A stopped session that does not close in time is dropped; nothing more is sent to it.
        }

        _lifetime.Dispose();
    }

    /// <summary>The session that carries the call on after the person's words were stopped, saying the supportive reply.</summary>
    private Task<IVoiceConsultantSession?> ResumeAsync(string said, CancellationToken token)
    {
        // Each carry-on may be reopened once more if its session fails or is stopped before the person speaks; the cap on
        // carry-ons bounds the reopens too.
        _resumes++;
        _recovered = false;
        _metrics.ConsultantResumed("person");
        LogResumed(_logger, "person");

        // Written while the new session opens, so it adds no wait when it is the quicker of the two.
        var words = WordsAsync(said, token);
        return Track(StartAsync(_base with { Opening = ConsultantOpening.Listen, Note = AssistantPrompt.CrisisResumeNote }, words, listen: true, token));
    }

    /// <summary>The last session of a call at its limit: it says the closing words, and nothing more is heard.</summary>
    private Task<IVoiceConsultantSession?> CloseAsync(CancellationToken token)
    {
        _metrics.ConsultantResumed("limit");
        LogResumed(_logger, "limit");
        return Track(StartAsync(
            _base with { Opening = ConsultantOpening.Listen, Note = AssistantPrompt.CrisisResumeNote },
            Task.FromResult(AssistantPrompt.CrisisClosingWords(_language)),
            listen: false,
            token));
    }

    /// <summary>
    /// Opens a session with <paramref name="request"/> and, with <paramref name="listen"/>, sends it the microphone; then has
    /// it say <paramref name="words"/>. Null when no session opens in time.
    /// </summary>
    private async Task<IVoiceConsultantSession?> StartAsync(VoiceConsultantRequest request, Task<string>? words, bool listen, CancellationToken token)
    {
        var leg = await OpenLegAsync(_model, request, _limits.ModelTimeout, token);
        if (leg is null)
        {
            LogStopped(_logger, "open_failed");
            return null;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                _retiring.Add(DisposeQuietlyAsync(leg));
                throw new OperationCanceledException(token);
            }

            _leg = leg;
        }

        // The microphone reaches the new session only once its words are ready, so nothing the person says while they
        // are written is half-heard by a session about to be told what to say.
        var said = words is null ? null : await words;
        if (listen)
        {
            Listen(leg);
        }

        if (said is not null)
        {
            await leg.SpeakAsync(said, token);
        }

        return leg;
    }

    /// <summary>The supportive words: written from what the person said, or the fixed words. Never throws.</summary>
    private async Task<string> WordsAsync(string said, CancellationToken token)
    {
        if (_crisis.IsConfigured)
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
            wait.CancelAfter(_limits.ConsultantCrisisReplyWait);
            try
            {
                var written = await _crisis.WriteAsync(_language, said, wait.Token).WaitAsync(wait.Token);
                if (written is not null && AssistantReplyGuard.Clean(written).ReplaceLineEndings(" ") is { Length: > 0 and <= MaxWords } words)
                {
                    _metrics.ModelCalled("crisis", succeeded: true);
                    return words;
                }
            }
            catch (Exception)
            {
                // Broad on purpose: a time-out, a provider fault or an adapter bug all mean the fixed words. Nothing is
                // logged from it, because an adapter's message could quote what the person said.
            }

            _metrics.ModelCalled("crisis", succeeded: false);
        }

        return AssistantPrompt.CrisisFallbackWords(_language);
    }

    /// <summary>
    /// The closing session's events until its words have been heard and their line settled, the session stops or ends, or
    /// <see cref="AssistantLimits.ConsultantClosingWait"/> passes.
    /// </summary>
    private async IAsyncEnumerable<VoiceConsultantEvent> ClosingEventsAsync(IVoiceConsultantSession leg, [EnumeratorCancellation] CancellationToken token)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
        wait.CancelAfter(_limits.ConsultantClosingWait);
        await using var events = leg.ReadEventsAsync(wait.Token).GetAsyncEnumerator(wait.Token);
        var spoke = false;
        while (await NextAsync(events, token))
        {
            var item = events.Current;
            if (item is ConsultantContentStopped)
            {
                yield break;
            }

            yield return item;
            spoke |= item is ConsultantAudio;
            if (spoke && item is ConsultantTranscript { Speaker: ConsultantSpeaker.Consultant, Final: true })
            {
                yield break;
            }
        }
    }

    /// <summary>The next event, or the fault that ended the session's stream instead. The call's own cancellation propagates.</summary>
    private static async Task<(bool More, Exception? Fault)> TryNextAsync(IAsyncEnumerator<VoiceConsultantEvent> events, CancellationToken token)
    {
        try
        {
            return (await events.MoveNextAsync(), null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return (false, exception);
        }
    }

    /// <summary>The next event; false at the end or once the closing wait ran out. The call's own cancellation propagates.</summary>
    private static async Task<bool> NextAsync(IAsyncEnumerator<VoiceConsultantEvent> events, CancellationToken token)
    {
        try
        {
            return await events.MoveNextAsync();
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>Sends the microphone to <paramref name="leg"/> (null: to nothing) and tells a frame waiting on a failure.</summary>
    private void Listen(IVoiceConsultantSession? leg)
    {
        TaskCompletionSource previous;
        lock (_gate)
        {
            _listening = leg;
            previous = _switched;
            _switched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        previous.TrySetResult();
    }

    private bool IsListening(IVoiceConsultantSession leg)
    {
        lock (_gate)
        {
            return ReferenceEquals(_listening, leg);
        }
    }

    private IVoiceConsultantSession? Current()
    {
        lock (_gate)
        {
            return _leg;
        }
    }

    private Task<IVoiceConsultantSession?> Track(Task<IVoiceConsultantSession?> task)
    {
        lock (_gate)
        {
            _resuming = task;
        }

        return task;
    }

    /// <summary>A stopped session is closed off the event path; disposing the call waits for it.</summary>
    private void Retire(IVoiceConsultantSession leg)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_leg, leg))
            {
                _leg = null;
            }

            _retiring.RemoveAll(task => task.IsCompleted);
            _retiring.Add(DisposeQuietlyAsync(leg));
        }
    }

    private static async Task DisposeQuietlyAsync(IVoiceConsultantSession leg)
    {
        try
        {
            await leg.DisposeAsync();
        }
        catch (Exception)
        {
            // Closing a stopped session is best effort; an adapter's message could quote content, so nothing is logged.
        }
    }

    private static InvalidOperationException Failed() => new("The voice model could not carry the call on.");

    /// <summary>A session that opens after its open gave up on it is closed at once, never left running.</summary>
    private static void CloseWhenOpened(Task<IVoiceConsultantSession>? opening)
    {
        if (opening is null)
        {
            return;
        }

        _ = opening.ContinueWith(
            static async task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    await DisposeQuietlyAsync(task.Result);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default).Unwrap();
    }

    [LoggerMessage(EventId = 1510, Level = LogLevel.Information, Message = "A live consultant call carried on after the provider stopped its model session: {Reason}.")]
    private static partial void LogResumed(ILogger logger, string reason);

    [LoggerMessage(EventId = 1511, Level = LogLevel.Warning, Message = "A live consultant call could not carry on: {Code}.")]
    private static partial void LogStopped(ILogger logger, string code);
}
