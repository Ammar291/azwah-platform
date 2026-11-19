using System.Text.RegularExpressions;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Domain.Safeguarding;

/// <summary>
/// A durable record that a person may be in danger or at risk, for staff to acknowledge and resolve. It holds fixed
/// codes only, never what the person said. Repeats of the same concern fold into one unresolved alert with a count.
/// </summary>
public sealed partial class SafetyAlert : AggregateRoot<SafetyAlertId>
{
    public const int MaxSignalCodeLength = 60;
    public const int MaxActorLength = 200;

    private SafetyAlert(SafetyAlertId id)
        : base(id)
    {
    }

    public PlatformUserId? UserId { get; private set; }

    public HouseholdId? HouseholdId { get; private set; }

    public FamilyMemberId? MemberId { get; private set; }

    public SafetyAlertSource Source { get; private set; }

    public SafetyConcern Concern { get; private set; }

    public SafetySeverity Severity { get; private set; }

    /// <summary>The detector of the latest occurrence.</summary>
    public SafetyDetector Detector { get; private set; }

    /// <summary>A phrase-rule code, a tier code or null; of the latest occurrence.</summary>
    public string? SignalCode { get; private set; }

    public string? Language { get; private set; }

    public AssessmentSessionId? AssessmentSessionId { get; private set; }

    public DateTimeOffset FirstDetectedAt { get; private set; }

    public DateTimeOffset LastDetectedAt { get; private set; }

    public int OccurrenceCount { get; private set; }

    public SafetyAlertStatus Status { get; private set; }

    public string? AcknowledgedBy { get; private set; }

    public DateTimeOffset? AcknowledgedAt { get; private set; }

    public string? ResolvedBy { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public SafetyAlertResolution? Resolution { get; private set; }

    public static SafetySeverity SeverityOf(SafetyConcern concern) =>
        concern == SafetyConcern.LowResilienceScore ? SafetySeverity.High : SafetySeverity.Critical;

    public static SafetyAlert Raise(
        SafetyAlertSubject subject,
        SafetyAlertSource source,
        SafetyConcern concern,
        SafetyDetector detector,
        string? signalCode,
        string? language,
        AssessmentSessionId? sessionId,
        DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(subject);
        RequireDefined(source);
        RequireDefined(concern);
        RequireDefined(detector);
        RequireSignalCode(signalCode);
        RequireUtc(at);
        if (language is not (null or "en" or "ar"))
        {
            throw new ArgumentException("The language must be en, ar or null.", nameof(language));
        }

        return new SafetyAlert(SafetyAlertId.New())
        {
            UserId = subject.UserId,
            HouseholdId = subject.HouseholdId,
            MemberId = subject.MemberId,
            Source = source,
            Concern = concern,
            Severity = SeverityOf(concern),
            Detector = detector,
            SignalCode = signalCode,
            Language = language,
            AssessmentSessionId = sessionId,
            FirstDetectedAt = at,
            LastDetectedAt = at,
            OccurrenceCount = 1,
            Status = SafetyAlertStatus.Open,
        };
    }

    /// <summary>
    /// Folds a repeat of the same concern into this alert. A repeat of an acknowledged alert re-opens it and clears the
    /// acknowledgement, so it goes back to the open queue and pages again; who acknowledged it stays in the audit trail.
    /// </summary>
    public Result Recur(SafetyDetector detector, string? signalCode, DateTimeOffset at)
    {
        RequireDefined(detector);
        RequireSignalCode(signalCode);
        RequireUtc(at);
        if (Status == SafetyAlertStatus.Resolved)
        {
            return Result.Failure(SafetyErrors.AlertResolved);
        }

        if (Status == SafetyAlertStatus.Acknowledged)
        {
            Status = SafetyAlertStatus.Open;
            AcknowledgedBy = null;
            AcknowledgedAt = null;
        }

        OccurrenceCount++;
        Detector = detector;
        SignalCode = signalCode;
        LastDetectedAt = at < LastDetectedAt ? LastDetectedAt : at;
        return Result.Success();
    }

    public Result Acknowledge(string by, DateTimeOffset at)
    {
        RequireActor(by);
        RequireUtc(at);
        if (Status != SafetyAlertStatus.Open)
        {
            return Result.Failure(SafetyErrors.AlertNotOpen);
        }

        Status = SafetyAlertStatus.Acknowledged;
        AcknowledgedBy = by;
        AcknowledgedAt = at < FirstDetectedAt ? FirstDetectedAt : at;
        return Result.Success();
    }

    public Result Resolve(SafetyAlertResolution resolution, string by, DateTimeOffset at)
    {
        RequireDefined(resolution);
        RequireActor(by);
        RequireUtc(at);
        if (Status == SafetyAlertStatus.Resolved)
        {
            return Result.Failure(SafetyErrors.AlertResolved);
        }

        Status = SafetyAlertStatus.Resolved;
        Resolution = resolution;
        ResolvedBy = by;
        ResolvedAt = at < FirstDetectedAt ? FirstDetectedAt : at;
        return Result.Success();
    }

    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SignalCodePattern();

    private static void RequireSignalCode(string? signalCode)
    {
        if (signalCode is not null && (signalCode.Length > MaxSignalCodeLength || !SignalCodePattern().IsMatch(signalCode)))
        {
            throw new ArgumentException("The signal code must be dotted snake case of at most 60 characters.", nameof(signalCode));
        }
    }

    private static void RequireActor(string by)
    {
        if (string.IsNullOrWhiteSpace(by) || by.Length > MaxActorLength)
        {
            throw new ArgumentException("The acting subject must be 1 to 200 characters.", nameof(by));
        }
    }

    private static void RequireUtc(DateTimeOffset at)
    {
        if (!UtcTime.IsUtc(at))
        {
            throw new ArgumentException("Times must be UTC.", nameof(at));
        }
    }

    private static void RequireDefined<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
    }
}
