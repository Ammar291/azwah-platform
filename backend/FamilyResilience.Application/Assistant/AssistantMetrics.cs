using System.Diagnostics.Metrics;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// Counters for the assistant and the consultant (D-126), meter <see cref="MeterName"/>. Tags are fixed codes only:
/// never a message, a reply, a caption, a tool's arguments or anything that identifies a person.
/// </summary>
public sealed class AssistantMetrics
{
    public const string MeterName = "FamilyResilience.Assistant";

    private readonly Counter<long> _replies;
    private readonly Histogram<double> _replyDuration;
    private readonly Counter<long> _modelCalls;
    private readonly Counter<long> _modelTokens;
    private readonly Counter<long> _toolCalls;
    private readonly Counter<long> _safetyFlags;
    private readonly Counter<long> _consultantSessions;
    private readonly Histogram<double> _consultantSeconds;
    private readonly Histogram<double> _consultantFirstAudio;
    private readonly Counter<long> _consultantResumes;

    public AssistantMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MeterName);
        _replies = meter.CreateCounter<long>("azwah.assistant.replies", description: "Assistant replies, by outcome.");
        _replyDuration = meter.CreateHistogram<double>(
            "azwah.assistant.reply_duration", unit: "ms", description: "Time to answer a chat message, by outcome.");
        _modelCalls = meter.CreateCounter<long>("azwah.assistant.model_calls", description: "Model calls, by kind and result.");
        _modelTokens = meter.CreateCounter<long>("azwah.assistant.model_tokens", description: "Model tokens, by kind and direction.");
        _toolCalls = meter.CreateCounter<long>("azwah.assistant.tool_calls", description: "Agent tool calls, by tool and outcome.");
        _safetyFlags = meter.CreateCounter<long>(
            "azwah.assistant.safety_flags", description: "Messages the safety check flagged, by channel.");
        _consultantSessions = meter.CreateCounter<long>(
            "azwah.consultant.sessions", description: "Live consultant sessions, by how they ended.");
        _consultantSeconds = meter.CreateHistogram<double>(
            "azwah.consultant.session_seconds", unit: "s", description: "Length of live consultant sessions, by how they ended.");
        _consultantFirstAudio = meter.CreateHistogram<double>(
            "azwah.consultant.first_audio", unit: "ms", description: "Time from the start message to the consultant's first sound.");
        _consultantResumes = meter.CreateCounter<long>(
            "azwah.consultant.resumes", description: "New model sessions that carried a live call on after the provider stopped one, by reason.");
    }

    /// <summary><paramref name="outcome"/>: <c>succeeded</c>, <c>model_failed</c> or <c>unavailable</c>.</summary>
    public void ReplyCompleted(string outcome, TimeSpan duration)
    {
        var tag = new KeyValuePair<string, object?>("outcome", outcome);
        _replies.Add(1, tag);
        _replyDuration.Record(duration.TotalMilliseconds, tag);
    }

    /// <summary><paramref name="kind"/>: <c>reply</c>, <c>memory</c>, <c>safety</c>, <c>delegate</c> or <c>crisis</c> (the supportive reply, D-141).</summary>
    public void ModelCalled(string kind, bool succeeded, AssistantModelUsage? usage = null)
    {
        _modelCalls.Add(1, new KeyValuePair<string, object?>("kind", kind), new KeyValuePair<string, object?>("result", succeeded ? "ok" : "failed"));
        if (usage is { } used)
        {
            Tokens(kind, used);
        }
    }

    public void Tokens(string kind, AssistantModelUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        if (usage.InputTokens > 0)
        {
            _modelTokens.Add(usage.InputTokens, new KeyValuePair<string, object?>("kind", kind), new KeyValuePair<string, object?>("direction", "input"));
        }

        if (usage.OutputTokens > 0)
        {
            _modelTokens.Add(usage.OutputTokens, new KeyValuePair<string, object?>("kind", kind), new KeyValuePair<string, object?>("direction", "output"));
        }
    }

    /// <summary><paramref name="tool"/> is a registered tool's name or <c>unknown</c>; the outcome a fixed code.</summary>
    public void ToolCalled(string tool, string outcome) =>
        _toolCalls.Add(1, new KeyValuePair<string, object?>("tool", tool), new KeyValuePair<string, object?>("outcome", outcome));

    /// <summary>
    /// <paramref name="channel"/>: <c>chat</c> or <c>voice</c> (our safety check), or <c>voice_filter</c> (OpenAI's
    /// filter stopped a call).
    /// </summary>
    public void SafetyFlagged(string channel) => _safetyFlags.Add(1, new KeyValuePair<string, object?>("channel", channel));

    public void ConsultantEnded(string ending, TimeSpan duration)
    {
        var tag = new KeyValuePair<string, object?>("ending", ending);
        _consultantSessions.Add(1, tag);
        _consultantSeconds.Record(duration.TotalSeconds, tag);
    }

    public void ConsultantFirstAudio(TimeSpan delay) => _consultantFirstAudio.Record(delay.TotalMilliseconds);

    /// <summary>
    /// <paramref name="reason"/> (D-141): <c>person</c> (the filter stopped the call over what the person said),
    /// <c>seeded</c> (it stopped before the person spoke, so over what the session was started with), <c>fault</c> (a
    /// session carrying the call on failed before the person spoke in it) or <c>limit</c> (the closing words of a call at
    /// its limit).
    /// </summary>
    public void ConsultantResumed(string reason) => _consultantResumes.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
