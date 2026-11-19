namespace FamilyResilience.Infrastructure.Assistant;

/// <summary>
/// When <see cref="OpenAiResponses"/> tries a call once more. A dropped connection always gets one more try; a 429 or 5xx
/// answer only with <paramref name="AfterBusyAnswer"/>. <paramref name="Pause"/> comes before the second try.
/// </summary>
internal readonly record struct OpenAiRetry(bool AfterBusyAnswer, TimeSpan Pause);
