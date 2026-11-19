namespace FamilyResilience.Domain.Common;

public static class UtcTime
{
    public static bool IsUtc(DateTimeOffset time) => time.Offset == TimeSpan.Zero;

    public static bool IsUtc(DateTimeOffset? time) => time is null || time.Value.Offset == TimeSpan.Zero;
}
