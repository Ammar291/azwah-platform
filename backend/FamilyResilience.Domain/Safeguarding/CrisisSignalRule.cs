namespace FamilyResilience.Domain.Safeguarding;

/// <summary>
/// One rule of the crisis phrase list. Phrases and exceptions are already normalised with
/// <see cref="CrisisTextNormalizer"/>. An occurrence of a phrase that shares a word with an occurrence of one of the
/// rule's <see cref="Exceptions"/> does not count ("hits me" in "my brother hits me up").
/// </summary>
public sealed record CrisisSignalRule(string Code, SafetyConcern Concern, IReadOnlyList<string> Phrases)
{
    public IReadOnlyList<string> Exceptions { get; init; } = [];
}
