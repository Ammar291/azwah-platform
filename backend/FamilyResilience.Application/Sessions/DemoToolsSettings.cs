namespace FamilyResilience.Application.Sessions;

/// <summary>
/// Demonstration tools (D-130), from configuration section <c>DemoTools</c>. Off unless <c>DemoTools:Enabled</c> parses
/// as <c>true</c>; only <c>appsettings.Development.json</c> switches them on, and a deployed API refuses to start with
/// them on (<c>SecurityConfigurationGuard</c>), because their answers are made up and must never reach real data.
/// </summary>
public sealed record DemoToolsSettings(bool Enabled)
{
    public const string EnabledKey = "DemoTools:Enabled";

    public static DemoToolsSettings Off { get; } = new(false);

    /// <summary>Reads the switch as the guard does (trimmed), so no spelling of true slips past either.</summary>
    public static DemoToolsSettings From(Func<string, string?> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        return new DemoToolsSettings(bool.TryParse(read(EnabledKey)?.Trim(), out var enabled) && enabled);
    }
}
