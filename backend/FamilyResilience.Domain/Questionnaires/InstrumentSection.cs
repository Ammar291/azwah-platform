using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Questionnaires;

/// <summary>A group of items: an area of the household assessment or one family-voices module (A, B, C).</summary>
public sealed class InstrumentSection : Entity<InstrumentSectionId>
{
    public const int MaxTitleLength = 300;

    private readonly List<InstrumentItem> _items = [];

    private InstrumentSection(InstrumentSectionId id)
        : base(id)
    {
    }

    public InstrumentVersionId InstrumentVersionId { get; private set; }

    public string Code { get; private set; } = null!;

    public LocalizedText Title { get; private set; } = null!;

    public int DisplayOrder { get; private set; }

    public IReadOnlyList<InstrumentItem> Items => _items.AsReadOnly();

    internal static InstrumentSection Create(
        InstrumentVersionId versionId, string code, LocalizedText title, int displayOrder) =>
        new(InstrumentSectionId.New())
        {
            InstrumentVersionId = versionId,
            Code = code,
            Title = title,
            DisplayOrder = displayOrder,
        };

    internal InstrumentItem AddItem(
        string code,
        int displayOrder,
        ItemType itemType,
        bool isRequired,
        string? scoringCategory,
        ScoringDirection scoringDirection,
        LocalizedText prompt)
    {
        var item = InstrumentItem.Create(
            InstrumentVersionId, Id, code, displayOrder, itemType, isRequired, scoringCategory, scoringDirection, prompt);
        _items.Add(item);
        return item;
    }
}
