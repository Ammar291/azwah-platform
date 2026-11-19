using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Application.Consent;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Application.Referrals;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Analytics;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Referrals;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.UnitTests.Application.Assistant;

/// <summary>Answers every subject with the same caller record, and counts the lookups.</summary>
internal sealed class FixedCallerQueries(CallerRecord? record) : IAccessQueries
{
    public int Lookups { get; private set; }

    public Task<CallerRecord?> FindCallerAsync(string subject, CancellationToken cancellationToken = default)
    {
        Lookups++;
        return Task.FromResult(record);
    }

    public Task<FamilyMemberId?> AssignmentMemberAsync(AssessmentAssignmentId assignmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult<FamilyMemberId?>(null);

    public Task<FamilyMemberId?> SessionMemberAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult<FamilyMemberId?>(null);

    public Task<string?> ExportRequesterAsync(ExportJobId exportJobId, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}

/// <summary>A caller lookup whose database is down: every lookup throws, with a message that must never be logged.</summary>
internal sealed class FailingCallerQueries : IAccessQueries
{
    public const string Secret = "synthetic-row-value";

    public Task<CallerRecord?> FindCallerAsync(string subject, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException($"Synthetic database fault quoting {Secret}.");

    public Task<FamilyMemberId?> AssignmentMemberAsync(AssessmentAssignmentId assignmentId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<FamilyMemberId?> SessionMemberAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<string?> ExportRequesterAsync(ExportJobId exportJobId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

internal sealed class InMemoryConsentRecords(params ConsentRecord[] records) : IConsentRecordRepository
{
    public Task<IReadOnlyList<ConsentRecord>> ListForMembersAsync(
        IReadOnlyCollection<FamilyMemberId> memberIds, int skip, int take, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ConsentRecord>>(
            records.Where(record => memberIds.Contains(record.FamilyMemberId)).Skip(skip).Take(take).ToList());

    public Task<int> CountForMembersAsync(IReadOnlyCollection<FamilyMemberId> memberIds, CancellationToken cancellationToken = default) =>
        Task.FromResult(records.Count(record => memberIds.Contains(record.FamilyMemberId)));

    public void Add(ConsentRecord consentRecord) => throw new NotSupportedException();

    public Task<ConsentRecord?> GetAsync(ConsentRecordId id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ConsentRecord?> GetActiveAsync(FamilyMemberId memberId, ConsentType consentType, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ConsentRecord?> GetActiveWithShareLockAsync(
        FamilyMemberId memberId, ConsentType consentType, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ConsentRecord>> ListForMemberAsync(FamilyMemberId memberId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

internal sealed class InMemoryConsentPolicies(params ConsentPolicyVersion[] policies) : IConsentPolicyVersionRepository
{
    public Task<ConsentPolicyVersion?> GetAsync(ConsentPolicyVersionId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(policies.FirstOrDefault(policy => policy.Id == id));

    public void Add(ConsentPolicyVersion policy) => throw new NotSupportedException();

    public Task<IReadOnlyList<ConsentPolicyVersion>> ListCurrentAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

internal sealed class InMemoryAssignmentReadModel(params ParticipantAssignmentResponse[] assignments) : IAssignmentReadModel
{
    public Task<IReadOnlyList<ParticipantAssignmentResponse>> ListAsync(
        IReadOnlyCollection<FamilyMemberId> memberIds, AssignmentStatus? status, int skip, int take, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ParticipantAssignmentResponse>>(
            Matching(memberIds, status).Skip(skip).Take(take).ToList());

    public Task<int> CountAsync(IReadOnlyCollection<FamilyMemberId> memberIds, AssignmentStatus? status, CancellationToken cancellationToken = default) =>
        Task.FromResult(Matching(memberIds, status).Count());

    public Task<ParticipantAssignmentResponse?> GetAsync(AssessmentAssignmentId id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    private IEnumerable<ParticipantAssignmentResponse> Matching(IReadOnlyCollection<FamilyMemberId> memberIds, AssignmentStatus? status) =>
        assignments.Where(assignment => memberIds.Contains(FamilyMemberId.From(assignment.MemberId)) && (status is null || assignment.Status == status));
}

internal sealed class InMemoryRecommendations(params SupportRecommendation[] recommendations) : ISupportRecommendationRepository
{
    public Task<IReadOnlyList<SupportRecommendation>> ListForMembersAsync(
        IReadOnlyCollection<FamilyMemberId> memberIds, int skip, int take, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SupportRecommendation>>(
            recommendations.Where(item => memberIds.Contains(item.FamilyMemberId)).Skip(skip).Take(take).ToList());

    public Task<int> CountForMembersAsync(IReadOnlyCollection<FamilyMemberId> memberIds, CancellationToken cancellationToken = default) =>
        Task.FromResult(recommendations.Count(item => memberIds.Contains(item.FamilyMemberId)));

    public void Add(SupportRecommendation recommendation) => throw new NotSupportedException();

    public Task<IReadOnlyList<SupportRecommendation>> ListForSessionAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>No referral exists: every recommendation in these tests was made without referral consent.</summary>
internal sealed class NoReferrals : ISupportReferralRepository
{
    public Task<IReadOnlyList<SupportReferral>> ListByIdsAsync(IReadOnlyCollection<SupportReferralId> ids, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SupportReferral>>([]);

    public void Add(SupportReferral referral) => throw new NotSupportedException();

    public Task<SupportReferral?> GetAsync(SupportReferralId id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<SupportReferral>> ListForSessionAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<SupportReferral>> ListLiveForConsentAsync(ConsentRecordId consentId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<SupportReferral>> ListForMembersAsync(
        IReadOnlyCollection<FamilyMemberId> memberIds, int skip, int take, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<int> CountForMembersAsync(IReadOnlyCollection<FamilyMemberId> memberIds, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

internal sealed class InMemorySessions(params AssessmentSession[] sessions) : IAssessmentSessionRepository
{
    public Task<IReadOnlyList<AssessmentSession>> ListForMembersAsync(
        IReadOnlyCollection<FamilyMemberId> memberIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AssessmentSession>>(sessions.Where(session => memberIds.Contains(session.FamilyMemberId)).ToList());

    public void Add(AssessmentSession session) => throw new NotSupportedException();

    public Task<AssessmentSession?> GetAsync(AssessmentSessionId id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<AssessmentSession?> GetLiveForAssignmentAsync(AssessmentAssignmentId assignmentId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<AssessmentSessionId?> FindLiveIdForAssignmentAsync(AssessmentAssignmentId assignmentId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<AssessmentSessionId>> ListInProgressIdsForMemberAsync(FamilyMemberId memberId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

internal sealed class InMemoryVersions(params InstrumentVersion[] versions) : IInstrumentVersionRepository
{
    public Task<InstrumentVersion?> GetAsync(InstrumentVersionId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(versions.FirstOrDefault(version => version.Id == id));

    public void Add(InstrumentVersion version) => throw new NotSupportedException();

    public Task<InstrumentVersion?> GetEffectiveApprovedAsync(InstrumentId instrumentId, DateTimeOffset at, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<InstrumentVersion?> GetByNumberAsync(InstrumentId instrumentId, int versionNumber, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<InstrumentVersionSummaryResponse>> ListSummariesAsync(
        InstrumentId instrumentId, int skip, int take, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<int> CountAsync(InstrumentId instrumentId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<bool> ExistsAsync(InstrumentVersionId id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<int> GetHighestVersionNumberAsync(InstrumentId instrumentId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>Score results by session, counting every read, so a test can prove a non-head's context never reads one.</summary>
internal sealed class InMemoryScoreResults(params ScoreResult[] results) : IScoreResultRepository
{
    public int Reads { get; private set; }

    public Task<IReadOnlyList<ScoreResult>> ListForSessionAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken = default)
    {
        Reads++;
        return Task.FromResult<IReadOnlyList<ScoreResult>>(results.Where(result => result.AssessmentSessionId == sessionId).ToList());
    }

    public void Add(ScoreResult result) => throw new NotSupportedException();

    public Task<ScoreResult?> GetForSessionAndRuleSetAsync(AssessmentSessionId sessionId, RuleSetId ruleSetId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>
/// A text model that plays one scripted step per call (the last repeats) and records every step and memory request. No
/// provider is involved.
/// </summary>
internal sealed class ScriptedAssistantModel(params Func<AssistantModelStep, CancellationToken, Task<Result<AssistantModelTurn>>>[] steps)
    : IAssistantModel
{
    private readonly List<AssistantModelStep> _steps = [];
    private readonly List<AssistantMemoryRequest> _memoryRequests = [];

    public bool IsConfigured { get; init; } = true;

    /// <summary>What a memory refresh answers; by default a synthetic summary.</summary>
    public Func<AssistantMemoryRequest, CancellationToken, Task<Result<string>>> Summary { get; init; } =
        (_, _) => Task.FromResult(Result.Success("- Synthetic memory"));

    public IReadOnlyList<AssistantModelStep> Steps
    {
        get
        {
            lock (_steps)
            {
                return [.. _steps];
            }
        }
    }

    public IReadOnlyList<AssistantMemoryRequest> MemoryRequests
    {
        get
        {
            lock (_memoryRequests)
            {
                return [.. _memoryRequests];
            }
        }
    }

    public Task<Result<AssistantModelTurn>> NextAsync(AssistantModelStep step, CancellationToken cancellationToken)
    {
        int count;
        lock (_steps)
        {
            _steps.Add(step);
            count = _steps.Count;
        }

        return steps[Math.Min(count, steps.Length) - 1](step, cancellationToken);
    }

    public Task<Result<string>> SummarizeAsync(AssistantMemoryRequest request, CancellationToken cancellationToken)
    {
        lock (_memoryRequests)
        {
            _memoryRequests.Add(request);
        }

        return Summary(request, cancellationToken);
    }

    public static Func<AssistantModelStep, CancellationToken, Task<Result<AssistantModelTurn>>> Reply(string text) =>
        (_, _) => Task.FromResult(Result.Success(AssistantModelTurn.Reply(text, new AssistantModelUsage(100, 20))));

    /// <summary>A step that asks for tools: each call is (name, arguments as JSON); the produced item is synthetic.</summary>
    public static Func<AssistantModelStep, CancellationToken, Task<Result<AssistantModelTurn>>> Calls(params (string Name, string Arguments)[] calls) =>
        (step, _) => Task.FromResult(Result.Success(new AssistantModelTurn(
            null,
            calls.Select((call, index) => new AssistantToolCall($"call_{step.Continuation.Count}_{index}", call.Name, call.Arguments)).ToList(),
            [new AssistantModelOutputItem($$"""{"type":"function_call","n":{{step.Continuation.Count}}}""")],
            AssistantModelUsage.None)));

    public static Func<AssistantModelStep, CancellationToken, Task<Result<AssistantModelTurn>>> Fail() =>
        (_, _) => Task.FromResult(Result.Failure<AssistantModelTurn>(AssistantErrors.ModelFailed));

    public static Func<AssistantModelStep, CancellationToken, Task<Result<AssistantModelTurn>>> Throw() =>
        (_, _) => Task.FromException<Result<AssistantModelTurn>>(new InvalidOperationException("Synthetic adapter fault."));

    /// <summary>Never answers and ignores the token, as a badly written adapter might.</summary>
    public static Func<AssistantModelStep, CancellationToken, Task<Result<AssistantModelTurn>>> HangIgnoringCancellation() =>
        (_, _) => new TaskCompletionSource<Result<AssistantModelTurn>>().Task;
}

/// <summary>Tools that answer from a function and record every call; nothing is read.</summary>
internal sealed class RecordingToolRunner(Func<AssistantToolCall, AssistantToolResult>? answer = null) : IAssistantToolRunner
{
    private readonly List<AssistantToolCall> _calls = [];

    public IReadOnlyList<AssistantToolDefinition> Definitions { get; init; } =
        [new(SearchSupportDirectoryTool.Name, "Synthetic directory.", """{"type":"object"}"""), new(SuggestAppScreenTool.Name, "Synthetic screens.", """{"type":"object"}""")];

    public IReadOnlyList<AssistantToolCall> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.. _calls];
            }
        }
    }

    public Task<AssistantToolResult> RunAsync(AssistantToolCall call, string language, CancellationToken cancellationToken)
    {
        lock (_calls)
        {
            _calls.Add(call);
        }

        return Task.FromResult(answer?.Invoke(call) ?? new AssistantToolResult("""{"ok":true}""", "ok", [], []));
    }
}

/// <summary>A safety check with a fixed verdict, after an optional delay.</summary>
internal sealed class FixedSafetyCheck(AssistantSafetyVerdict verdict, TimeSpan? delay = null) : IAssistantSafetyCheck
{
    public bool IsConfigured { get; init; } = true;

    public List<string> Checked { get; } = [];

    public async Task<AssistantSafetyVerdict> CheckAsync(string text, CancellationToken cancellationToken)
    {
        lock (Checked)
        {
            Checked.Add(text);
        }

        if (delay is { } wait)
        {
            await Task.Delay(wait, cancellationToken);
        }

        return verdict;
    }
}

/// <summary>Metrics on a meter factory of their own, so tests never share counters.</summary>
internal static class TestAssistantMetrics
{
    public static AssistantMetrics Create() => new(new TestMeterFactory());

    private sealed class TestMeterFactory : System.Diagnostics.Metrics.IMeterFactory
    {
        private readonly List<System.Diagnostics.Metrics.Meter> _meters = [];

        public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options)
        {
            var meter = new System.Diagnostics.Metrics.Meter(options);
            _meters.Add(meter);
            return meter;
        }

        public void Dispose() => _meters.ForEach(meter => meter.Dispose());
    }
}

/// <summary>A voice model whose sessions do nothing; each open follows <see cref="Open"/>, and opened sessions are kept.</summary>
internal sealed class ScriptedVoiceModel : IVoiceConsultantModel
{
    public bool IsConfigured { get; init; } = true;

    public Func<CancellationToken, Task<IVoiceConsultantSession>> Open { get; init; } =
        _ => Task.FromResult<IVoiceConsultantSession>(new SilentVoiceSession());

    public List<VoiceConsultantRequest> Requests { get; } = [];

    public Task<IVoiceConsultantSession> OpenAsync(VoiceConsultantRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Open(cancellationToken);
    }
}

internal sealed class SilentVoiceSession : IVoiceConsultantSession
{
    public bool Disposed { get; private set; }

    public Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken cancellationToken) => Task.CompletedTask;

    public async IAsyncEnumerable<VoiceConsultantEvent> ReadEventsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        yield break;
    }

    public Task SpeakAsync(string words, CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// One model session a test drives (D-141): the test pushes the events the model would produce and reads the audio and
/// the words sent to it. Nothing leaves the test.
/// </summary>
internal sealed class ScriptedLeg : IVoiceConsultantSession
{
    private readonly System.Threading.Channels.Channel<VoiceConsultantEvent> _events =
        System.Threading.Channels.Channel.CreateUnbounded<VoiceConsultantEvent>();

    private readonly List<byte[]> _audio = [];
    private readonly List<string> _spoken = [];
    private readonly TaskCompletionSource _spoke = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    /// <summary>Thrown by every send, as a model whose socket has gone.</summary>
    public Exception? FailSends { get; set; }

    /// <summary>When set, every send waits for it before it goes (or fails).</summary>
    public TaskCompletionSource? SendGate { get; set; }

    public bool Disposed => Volatile.Read(ref _disposed) == 1;

    public IReadOnlyList<byte[]> Audio
    {
        get
        {
            lock (_audio)
            {
                return [.. _audio];
            }
        }
    }

    public IReadOnlyList<string> Spoken
    {
        get
        {
            lock (_spoken)
            {
                return [.. _spoken];
            }
        }
    }

    /// <summary>Completes once the session was asked to say something.</summary>
    public Task SpokeAsync => _spoke.Task;

    public void Push(params VoiceConsultantEvent[] items)
    {
        foreach (var item in items)
        {
            _events.Writer.TryWrite(item);
        }
    }

    /// <summary>The model ends the session.</summary>
    public void End() => _events.Writer.TryComplete();

    /// <summary>The model's stream fails.</summary>
    public void Fail() => _events.Writer.TryComplete(new InvalidOperationException("Synthetic model fault."));

    public async Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken cancellationToken)
    {
        if (SendGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        if (FailSends is { } failure)
        {
            throw failure;
        }

        lock (_audio)
        {
            _audio.Add(pcm.ToArray());
        }
    }

    public IAsyncEnumerable<VoiceConsultantEvent> ReadEventsAsync(CancellationToken cancellationToken) =>
        _events.Reader.ReadAllAsync(cancellationToken);

    public Task SpeakAsync(string words, CancellationToken cancellationToken)
    {
        lock (_spoken)
        {
            _spoken.Add(words);
        }

        _spoke.TrySetResult();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        _events.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// A voice model that opens the given sessions in turn and records every request; once they run out, opening fails.
/// </summary>
internal sealed class LegsVoiceModel(params IVoiceConsultantSession[] legs) : IVoiceConsultantModel
{
    private readonly Queue<IVoiceConsultantSession> _legs = new(legs);
    private readonly List<VoiceConsultantRequest> _requests = [];

    public bool IsConfigured => true;

    public IReadOnlyList<VoiceConsultantRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    public Task<IVoiceConsultantSession> OpenAsync(VoiceConsultantRequest request, CancellationToken cancellationToken)
    {
        lock (_requests)
        {
            _requests.Add(request);
            return _legs.TryDequeue(out var leg)
                ? Task.FromResult(leg)
                : Task.FromException<IVoiceConsultantSession>(new InvalidOperationException("Synthetic open fault."));
        }
    }
}

/// <summary>A supportive-reply writer that answers from a function and records what it was given (D-141).</summary>
internal sealed class ScriptedCrisisReply : IConsultantCrisisReply
{
    public const string Words = "Synthetic supportive words.";

    private readonly List<(string Language, string Said)> _calls = [];

    public bool IsConfigured { get; init; } = true;

    public Func<CancellationToken, Task<string?>> Write { get; init; } = _ => Task.FromResult<string?>(Words);

    public IReadOnlyList<(string Language, string Said)> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.. _calls];
            }
        }
    }

    public Task<string?> WriteAsync(string language, string said, CancellationToken cancellationToken)
    {
        lock (_calls)
        {
            _calls.Add((language, said));
        }

        return Write(cancellationToken);
    }
}
