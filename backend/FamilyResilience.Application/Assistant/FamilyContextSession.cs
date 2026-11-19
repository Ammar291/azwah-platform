using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// One sitting of answers: every answered question with the chosen answer, in the context's language and in question
/// order. <see cref="Result"/> is set only when the caller is the household's head and the session has been scored.
/// </summary>
public sealed record FamilyContextSession(
    string Member,
    string InstrumentName,
    string? SectionTitle,
    string Wave,
    SessionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? SubmittedAt,
    IReadOnlyList<FamilyContextAnswer> Answers,
    FamilyContextResult? Result)
{
    /// <summary>Never prints answers or results, so they cannot reach a log through string formatting.</summary>
    public override string ToString() => $"FamilyContextSession {{ Status = {Status}, Answers = {Answers.Count}, HasResult = {Result is not null} }}";
}
