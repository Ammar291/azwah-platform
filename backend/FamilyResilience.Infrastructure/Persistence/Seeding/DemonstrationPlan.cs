using FamilyResilience.Domain.Members;

namespace FamilyResilience.Infrastructure.Persistence.Seeding;

/// <summary>
/// The demonstration's fixed timeline, relative to the moment it is built (whole seconds, UTC): approvals and wave one
/// about seven months back, wave one closed about four months back, wave two open since about three weeks ago and
/// closing in about ten weeks.
/// </summary>
internal sealed record DemonstrationTimeline(DateTimeOffset Now)
{
    public DateTimeOffset ContentImported => Now.AddDays(-245);

    public DateTimeOffset Approved => Now.AddDays(-238);

    public DateTimeOffset WaveOnePlanned => Now.AddDays(-232);

    public DateTimeOffset WaveOneOpens => Now.AddDays(-210);

    public DateTimeOffset WaveOneCloses => Now.AddDays(-120);

    public DateTimeOffset WaveTwoPlanned => Now.AddDays(-35);

    public DateTimeOffset WaveTwoOpens => Now.AddDays(-21);

    public DateTimeOffset WaveTwoCloses => Now.AddDays(70);

    public static DemonstrationTimeline At(DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        return new DemonstrationTimeline(new DateTimeOffset(utc.UtcTicks - (utc.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero));
    }
}

/// <summary>How far one sitting got in its wave.</summary>
internal enum DemonstrationOutcome
{
    /// <summary>Assigned and never started: still waiting in an open wave, expired in a closed one.</summary>
    NotStarted = 1,

    /// <summary>Started with some answers; the wave closed first, so maintenance abandoned it and expired the assignment.</summary>
    Abandoned = 2,

    /// <summary>Started with some answers in the open wave.</summary>
    InProgress = 3,

    /// <summary>Every item answered, submitted and scored by the approved rule set.</summary>
    Completed = 4,
}

/// <summary>One member of a synthetic roster: an age in whole years on the day the demonstration is built.</summary>
internal sealed record DemonstrationMember(
    int Age,
    int BirthMonth,
    HouseholdRelationship? Relationship,
    EmploymentStatus? Employment,
    bool IsHead,
    bool IsPrimaryEarner);

/// <summary>An answer as the raw option value chosen for an item, and when it was given.</summary>
internal sealed record DemonstrationAnswer(string ItemCode, int RawValue, DateTimeOffset AnsweredAt);

/// <summary>
/// The head's household assessment in one wave. <c>Answers</c> are in display order: all 39 for a completed sitting,
/// the first few for one in progress or abandoned, none when not started.
/// </summary>
internal sealed record DemonstrationSitting(
    DemonstrationOutcome Outcome,
    DateTimeOffset AssignedAt,
    DateTimeOffset? StartedAt,
    IReadOnlyList<DemonstrationAnswer> Answers,
    DateTimeOffset? SubmittedAt);

/// <summary>During wave two the head of household changes: the former head becomes the new head's spouse.</summary>
internal sealed record DemonstrationHeadChange(int NewHeadIndex, HouseholdRelationship FormerHeadRelationship, DateTimeOffset At);

/// <summary>
/// One synthetic household: no names or contact details. <c>Members[0]</c> is the head when it enrols. Consent is
/// granted for <c>ConsentingMembers</c> (participation always, research use when <c>ResearchUse</c>).
/// </summary>
internal sealed record DemonstrationHousehold(
    int Number,
    string Reference,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RosterConfirmedAt,
    IReadOnlyList<DemonstrationMember> Members,
    IReadOnlyList<int> ConsentingMembers,
    bool ResearchUse,
    DemonstrationSitting? WaveOne,
    DemonstrationSitting? WaveTwo,
    DemonstrationHeadChange? HeadChange,
    DateTimeOffset? WithdrawnAt,
    bool IsLeadPersonaHousehold)
{
    public DateTimeOffset ConsentedAt => CreatedAt.AddMinutes(30);

    /// <summary>Whether the household takes part from wave one (enrolled before it opened).</summary>
    public bool InWaveOne => WaveOne is not null;
}

/// <summary>Everything the demonstration database holds about households, in household-number order.</summary>
internal sealed record DemonstrationPlan(DemonstrationTimeline Timeline, IReadOnlyList<DemonstrationHousehold> Households)
{
    /// <summary>When the household-member persona's account is linked to the lead's spouse.</summary>
    public DateTimeOffset MemberPersonaLinkedAt => Timeline.WaveOneOpens.AddDays(30);
}
