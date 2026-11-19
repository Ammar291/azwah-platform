using FamilyResilience.Domain.Safeguarding;

namespace FamilyResilience.Application.Safeguarding;

/// <summary>
/// What a detector noticed, as fixed codes only: never the person's text. <paramref name="SignalCode"/> is a phrase-rule
/// code (<c>suicide.intent</c>), a tier code (<c>vulnerable</c>) or null; <paramref name="Language"/> is <c>en</c>,
/// <c>ar</c> or null. The subject is never part of a signal: the recorder takes it from the server.
/// </summary>
public sealed record SafetySignal(
    SafetyAlertSource Source, SafetyConcern Concern, SafetyDetector Detector, string? SignalCode, string? Language);
