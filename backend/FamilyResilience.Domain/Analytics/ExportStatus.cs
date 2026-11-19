namespace FamilyResilience.Domain.Analytics;

/// <summary>Queued, then Running, then Completed or Failed; a Completed export becomes Expired once its file is deleted.</summary>
public enum ExportStatus
{
    Queued = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
    Expired = 5,
}
