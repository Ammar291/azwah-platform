namespace FamilyResilience.Application.Assistant;

/// <summary>
/// The fixed numbers of the live consultant's protocol, version 2 (D-124). Audio is PCM 16-bit little-endian mono both
/// ways, at the rate GPT-Live takes and gives, so the server resamples nothing.
/// </summary>
public static class ConsultantProtocol
{
    public const int Version = 2;

    /// <summary>The microphone audio the app sends, in samples a second.</summary>
    public const int InputSampleRate = 24_000;

    /// <summary>The consultant's audio the server sends, in samples a second.</summary>
    public const int OutputSampleRate = 24_000;

    /// <summary>The largest message the app may send: 64 KB. A larger one closes the session.</summary>
    public const int MaxFrameBytes = 64 * 1024;
}
