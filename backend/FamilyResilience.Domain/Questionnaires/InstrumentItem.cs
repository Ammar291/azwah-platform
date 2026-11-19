using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Questionnaires;

/// <summary>A question inside a section of one instrument version.</summary>
public sealed class InstrumentItem : Entity<InstrumentItemId>
{
    public const int MaxPromptLength = 2000;

    private readonly List<ResponseOption> _options = [];

    private InstrumentItem(InstrumentItemId id)
        : base(id)
    {
    }

    public InstrumentVersionId InstrumentVersionId { get; private set; }

    public InstrumentSectionId InstrumentSectionId { get; private set; }

    public string Code { get; private set; } = null!;

    public int DisplayOrder { get; private set; }

    public ItemType ItemType { get; private set; }

    public bool IsRequired { get; private set; }

    public string? ScoringCategory { get; private set; }

    public ScoringDirection ScoringDirection { get; private set; }

    public LocalizedText Prompt { get; private set; } = null!;

    public IReadOnlyList<ResponseOption> Options => _options.AsReadOnly();

    public ResponseOption? FindOption(ResponseOptionId optionId) => _options.Find(option => option.Id == optionId);

    internal static InstrumentItem Create(
        InstrumentVersionId versionId,
        InstrumentSectionId sectionId,
        string code,
        int displayOrder,
        ItemType itemType,
        bool isRequired,
        string? scoringCategory,
        ScoringDirection scoringDirection,
        LocalizedText prompt) =>
        new(InstrumentItemId.New())
        {
            InstrumentVersionId = versionId,
            InstrumentSectionId = sectionId,
            Code = code,
            DisplayOrder = displayOrder,
            ItemType = itemType,
            IsRequired = isRequired,
            ScoringCategory = scoringCategory,
            ScoringDirection = scoringDirection,
            Prompt = prompt,
        };

    /// <summary>The options that are points on the scale (every option except a non-substantive one).</summary>
    public IEnumerable<ResponseOption> GetSubstantiveOptions() => _options.Where(option => !option.IsNonSubstantive);

    /// <summary>Whether the substantive values are consecutive integers (for example 1–5), so a mirrored value stays on the scale.</summary>
    public bool HasConsecutiveScale()
    {
        var values = GetSubstantiveOptions().Select(option => option.Value).Order().ToList();
        return values.Count >= 2 && values[^1] - values[0] == values.Count - 1;
    }

    internal ResponseOption AddOption(string code, int displayOrder, int value, LocalizedText label, bool isNonSubstantive)
    {
        var option = ResponseOption.Create(Id, code, displayOrder, value, label, isNonSubstantive);
        _options.Add(option);
        return option;
    }
}
