namespace FamilyResilience.Application.Assistant;

/// <summary>
/// The app places a reply may offer as a button (D-122). The app maps each code to its own route and translated label;
/// nothing navigates by itself.
/// </summary>
public static class AssistantScreens
{
    public const string Sos = "sos";
    public const string Services = "services";
    public const string Home = "home";
    public const string Resilience = "resilience";
    public const string Help = "help";
    public const string Consultant = "consultant";
    public const string Assistant = "assistant";

    /// <summary>The most a reply offers.</summary>
    public const int MaxPerReply = 3;

    public static IReadOnlyList<string> All { get; } = [Sos, Services, Home, Resilience, Help, Consultant, Assistant];

    public static bool IsKnown(string? code) => code is not null && All.Contains(code, StringComparer.Ordinal);

    /// <summary>Distinct known codes, SOS first, at most <see cref="MaxPerReply"/>.</summary>
    public static IReadOnlyList<string> Order(IEnumerable<string> codes) =>
        codes.Where(IsKnown).Distinct(StringComparer.Ordinal).OrderBy(code => code == Sos ? 0 : 1).Take(MaxPerReply).ToList();
}
