using System.Text;
using FamilyResilience.Application.Assistant;

namespace FamilyResilience.Infrastructure.Assistant;

/// <summary>
/// Turns GPT-Live's transcript deltas (a few words each, with their place in the stream) into the protocol's lines
/// (D-124): one growing line per speaker, final when the other speaker starts, when the same speaker resumes after
/// <see cref="Pause"/>, when the consultant falls silent, or when the call ends. One instance per live session; not
/// thread-safe.
/// </summary>
public sealed class LiveTranscriptLines
{
    public const int Pause = 1_500;

    /// <summary>A line longer than this is closed and a new one started, so a caption stays readable and within the protocol.</summary>
    public const int MaxLineLength = 1_500;

    private readonly StringBuilder _text = new();
    private ConsultantSpeaker? _speaker;
    private long _endMs;

    /// <summary>The lines this delta settles or grows, in order: a final line first when one ends, then the growing one.</summary>
    public IReadOnlyList<ConsultantTranscript> Add(ConsultantSpeaker speaker, string delta, long startMs, long endMs)
    {
        var lines = new List<ConsultantTranscript>(2);
        if (_speaker is { } open && (open != speaker || startMs - _endMs > Pause || _text.Length + delta.Length > MaxLineLength))
        {
            if (Close() is { } closed)
            {
                lines.Add(closed);
            }
        }

        _speaker = speaker;
        _text.Append(delta);
        _endMs = Math.Max(_endMs, endMs);
        if (_text.ToString().Trim() is { Length: > 0 } growing)
        {
            lines.Add(new ConsultantTranscript(speaker, growing, Final: false));
        }

        return lines;
    }

    /// <summary>The open line as final when <paramref name="speaker"/> holds it (the consultant fell silent), else null.</summary>
    public ConsultantTranscript? CloseFor(ConsultantSpeaker speaker) => _speaker == speaker ? Close() : null;

    /// <summary>The open line as final, or null when none is open.</summary>
    public ConsultantTranscript? Close()
    {
        if (_speaker is not { } speaker)
        {
            return null;
        }

        var text = _text.ToString().Trim();
        _text.Clear();
        _speaker = null;
        return text.Length == 0 ? null : new ConsultantTranscript(speaker, text, Final: true);
    }
}
