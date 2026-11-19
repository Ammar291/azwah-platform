namespace FamilyResilience.Application.SupportResources;

/// <summary>One cleaned word of a search. Arabic terms of three or more letters match as prefixes.</summary>
public sealed record SearchTerm(string Text, SearchTermKind Kind)
{
    public bool IsPrefix => Kind == SearchTermKind.Arabic && Text.Length >= 3;
}
