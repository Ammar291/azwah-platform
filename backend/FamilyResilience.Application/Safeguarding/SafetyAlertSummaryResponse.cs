namespace FamilyResilience.Application.Safeguarding;

/// <summary>
/// Counts only, no personal data: open alerts, acknowledged ones, open critical ones, and the latest detection among
/// unresolved alerts (null when there are none).
/// </summary>
public sealed record SafetyAlertSummaryResponse(int Open, int Acknowledged, int OpenCritical, DateTimeOffset? LatestDetectedAt);
