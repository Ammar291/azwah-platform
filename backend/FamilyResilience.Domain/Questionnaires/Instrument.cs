using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Questionnaires;

/// <summary>A questionnaire family: the household assessment or the family-voices modules.</summary>
public sealed class Instrument : AggregateRoot<InstrumentId>
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 2000;

    private Instrument(InstrumentId id)
        : base(id)
    {
    }

    public string Code { get; private set; } = null!;

    public InstrumentKind Kind { get; private set; }

    public LocalizedText Name { get; private set; } = null!;

    public LocalizedText Description { get; private set; } = null!;

    public InstrumentStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<Instrument> Create(
        string code, InstrumentKind kind, LocalizedText name, LocalizedText description, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(description);

        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure<Instrument>(CommonErrors.TimeNotUtc);
        }

        if (!Codes.IsSnakeCode(code))
        {
            return Result.Failure<Instrument>(QuestionnaireErrors.InstrumentCodeInvalid);
        }

        if (!Enum.IsDefined(kind))
        {
            return Result.Failure<Instrument>(QuestionnaireErrors.InstrumentKindInvalid);
        }

        if (!name.FitsWithin(MaxNameLength) || !description.FitsWithin(MaxDescriptionLength))
        {
            return Result.Failure<Instrument>(QuestionnaireErrors.TextTooLong);
        }

        return Result.Success(new Instrument(InstrumentId.New())
        {
            Code = code,
            Kind = kind,
            Name = name,
            Description = description,
            Status = InstrumentStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    public Result Retire(DateTimeOffset now)
    {
        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure(CommonErrors.TimeNotUtc);
        }

        if (Status == InstrumentStatus.Retired)
        {
            return Result.Failure(QuestionnaireErrors.InstrumentAlreadyRetired);
        }

        Status = InstrumentStatus.Retired;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Replaces the name and description, for example to add the Arabic that a draft instrument lacked. The new text
    /// must be bilingual, so an instrument's text never becomes less complete.
    /// </summary>
    public Result ChangeText(LocalizedText name, LocalizedText description, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(description);

        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure(CommonErrors.TimeNotUtc);
        }

        if (Status != InstrumentStatus.Active)
        {
            return Result.Failure(QuestionnaireErrors.InstrumentNotActive);
        }

        if (!name.IsComplete || !description.IsComplete)
        {
            return Result.Failure(CommonErrors.TextRequired);
        }

        if (!name.FitsWithin(MaxNameLength) || !description.FitsWithin(MaxDescriptionLength))
        {
            return Result.Failure(QuestionnaireErrors.TextTooLong);
        }

        Name = name;
        Description = description;
        UpdatedAt = now;
        return Result.Success();
    }
}
