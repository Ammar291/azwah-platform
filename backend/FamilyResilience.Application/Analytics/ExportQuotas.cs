namespace FamilyResilience.Application.Analytics;

/// <summary>
/// How many exports one researcher may have, from configuration section <c>Exports</c>: at most
/// <c>MaxActivePerRequester</c> queued or running (default 2, at most 10) and <c>MaxPerRequesterPerDay</c> requested in
/// any 24 hours (default 20, at most 200). They keep a researcher from turning the export worker into a bulk copy of the
/// research data.
/// </summary>
public sealed record ExportQuotas(int MaxActivePerRequester, int MaxPerRequesterPerDay)
{
    public const int DefaultMaxActivePerRequester = 2;
    public const int DefaultMaxPerRequesterPerDay = 20;

    public const int MaxMaxActivePerRequester = 10;
    public const int MaxMaxPerRequesterPerDay = 200;

    public static ExportQuotas Default { get; } = new(DefaultMaxActivePerRequester, DefaultMaxPerRequesterPerDay);

    public static ExportQuotas Bounded(int maxActivePerRequester, int maxPerRequesterPerDay) =>
        new(
            Math.Clamp(maxActivePerRequester, 1, MaxMaxActivePerRequester),
            Math.Clamp(maxPerRequesterPerDay, 1, MaxMaxPerRequesterPerDay));
}
