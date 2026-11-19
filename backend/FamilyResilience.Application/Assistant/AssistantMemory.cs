namespace FamilyResilience.Application.Assistant;

/// <summary>
/// The rolling memory of a conversation (D-123): a summary the server writes of what no longer fits the window, kept
/// sealed on the phone and sent back with each request. Once a request holds <see cref="RefreshAt"/> messages, the
/// server summarises the old memory and every message but the latest <see cref="KeepRecent"/>, so nothing that later
/// falls out of the 40-message window is lost.
/// </summary>
public static class AssistantMemory
{
    /// <summary>The longest memory the app may send or the server returns.</summary>
    public const int MaxLength = 2_000;

    /// <summary>A request with this many messages or more refreshes the memory.</summary>
    public const int RefreshAt = 30;

    /// <summary>The latest messages a refresh leaves out of the summary, because the app still sends them.</summary>
    public const int KeepRecent = 20;

    /// <summary>The trimmed memory, or null when there is none.</summary>
    public static string? Normalise(string? memory) => string.IsNullOrWhiteSpace(memory) ? null : memory.Trim();

    /// <summary>A model's summary cleaned like a reply and cut to <see cref="MaxLength"/>; null when nothing is left.</summary>
    public static string? FromModel(string? summary)
    {
        var cleaned = AssistantReplyGuard.Clean(summary ?? string.Empty);
        if (cleaned.Length == 0)
        {
            return null;
        }

        return cleaned.Length <= MaxLength ? cleaned : cleaned[..MaxLength].TrimEnd();
    }
}
