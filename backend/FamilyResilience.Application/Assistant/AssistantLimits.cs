namespace FamilyResilience.Application.Assistant;

/// <summary>
/// Time limits for the assistant and the live consultant. <see cref="Default"/> is what the API uses; tests register
/// shorter ones.
/// </summary>
/// <param name="ModelTimeout">How long one model call (a reply, or opening a live session) may take: 30 seconds.</param>
/// <param name="ConsultantSessionLimit">How long a live session may last: 15 minutes.</param>
/// <param name="ConsultantIdleLimit">How long a live session may go without audio from the app: 60 seconds.</param>
/// <param name="ConsultantCloseTimeout">How long the server waits for the app's close frame after its own: 5 seconds.</param>
public sealed record AssistantLimits(
    TimeSpan ModelTimeout, TimeSpan ConsultantSessionLimit, TimeSpan ConsultantIdleLimit, TimeSpan ConsultantCloseTimeout)
{
    /// <summary>How long the server waits for the app's <c>start</c> message after the upgrade: 5 seconds (D-124).</summary>
    public TimeSpan ConsultantStartWait { get; init; } = TimeSpan.FromSeconds(5);

    public static AssistantLimits Default { get; } =
        new(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5));

    /// <summary>How long writing one score report may take: 90 seconds, because a whole report is far longer than a reply.</summary>
    public TimeSpan ScoreReportTimeout { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// How many times one call carries on after the provider's filter stopped it over what the person said: 5 (D-141). The
    /// next stop ends the call after <see cref="AssistantPrompt.CrisisClosingWords"/>. Each carry-on is a new model
    /// session and a short model call, and only the person's own words can cause one, so the cap stops a loop, not a
    /// conversation; the 15-minute session limit still applies.
    /// </summary>
    public int ConsultantResumeLimit { get; init; } = 5;

    /// <summary>
    /// How long the supportive reply may take to write, counted from the stop: 5 seconds (D-141). It is written while the
    /// new session opens (measured 2.3 to 3.5 s); past this the fixed words are said instead.
    /// </summary>
    public TimeSpan ConsultantCrisisReplyWait { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long the safety check of a call's earlier conversation may take: 2.5 seconds (D-141). It runs while GPT-Live
    /// connects (1.5 to 3.4 s), so it rarely holds the start up. A warm check took 0.7 s and a cold one 1.9 to 2.6 s on
    /// 2026-11-10; a check cut at 1.5 s let a line through, and the reopen that caught it cost the call 3 to 4.5 s and
    /// its whole history. Lines not cleared in time go through on the phrase screen alone.
    /// </summary>
    public TimeSpan ConsultantHistoryCheckWait { get; init; } = TimeSpan.FromSeconds(2.5);

    /// <summary>How long the closing words may play before a call at its limit ends: 30 seconds (D-141).</summary>
    public TimeSpan ConsultantClosingWait { get; init; } = TimeSpan.FromSeconds(30);
}
