using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Questionnaires;

/// <summary>
/// An answer choice. <see cref="Value"/> is the raw answer value stored with each response. A non-substantive option
/// (for example "99 Prefer not to answer") keeps its raw value but is never a point on the scale.
/// </summary>
public sealed class ResponseOption : Entity<ResponseOptionId>
{
    public const int MaxLabelLength = 300;

    private ResponseOption(ResponseOptionId id)
        : base(id)
    {
    }

    public InstrumentItemId InstrumentItemId { get; private set; }

    public string Code { get; private set; } = null!;

    public int DisplayOrder { get; private set; }

    public int Value { get; private set; }

    public LocalizedText Label { get; private set; } = null!;

    public bool IsNonSubstantive { get; private set; }

    internal static ResponseOption Create(
        InstrumentItemId itemId, string code, int displayOrder, int value, LocalizedText label, bool isNonSubstantive) =>
        new(ResponseOptionId.New())
        {
            InstrumentItemId = itemId,
            Code = code,
            DisplayOrder = displayOrder,
            Value = value,
            Label = label,
            IsNonSubstantive = isNonSubstantive,
        };
}
