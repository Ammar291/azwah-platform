using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Questionnaires;

/// <summary>
/// An exact version of an instrument with its sections, items and options. Content changes only while Draft;
/// approval publishes it and makes it immutable (enforced again by a database trigger). Responses always point at
/// the exact version answered.
/// </summary>
public sealed class InstrumentVersion : AggregateRoot<InstrumentVersionId>
{
    public const int MaxContentReferenceLength = 300;

    private readonly List<InstrumentSection> _sections = [];

    private InstrumentVersion(InstrumentVersionId id)
        : base(id)
    {
    }

    public InstrumentId InstrumentId { get; private set; }

    public int VersionNumber { get; private set; }

    public InstrumentVersionStatus Status { get; private set; }

    /// <summary>Which source document and revision the Arabic text came from.</summary>
    public string? ContentReferenceAr { get; private set; }

    /// <summary>Which source document and revision the English text came from.</summary>
    public string? ContentReferenceEn { get; private set; }

    public string? ApprovedBy { get; private set; }

    /// <summary>When the version was approved, which is when it was published.</summary>
    public DateTimeOffset? ApprovedAt { get; private set; }

    public string? ApprovalSource { get; private set; }

    public DateTimeOffset? EffectiveFrom { get; private set; }

    public DateTimeOffset? EffectiveTo { get; private set; }

    public DateTimeOffset? RetiredAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<InstrumentSection> Sections => _sections.AsReadOnly();

    public static Result<InstrumentVersion> CreateDraft(Instrument instrument, int versionNumber, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(instrument);

        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure<InstrumentVersion>(CommonErrors.TimeNotUtc);
        }

        if (instrument.Status != InstrumentStatus.Active)
        {
            return Result.Failure<InstrumentVersion>(QuestionnaireErrors.InstrumentNotActive);
        }

        if (versionNumber < 1)
        {
            return Result.Failure<InstrumentVersion>(QuestionnaireErrors.VersionNumberInvalid);
        }

        return Result.Success(new InstrumentVersion(InstrumentVersionId.New())
        {
            InstrumentId = instrument.Id,
            VersionNumber = versionNumber,
            Status = InstrumentVersionStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    /// <summary>
    /// A new draft holding a copy of every section, item and option of <paramref name="source"/> (same codes, order,
    /// flags and text; new IDs). This is how published content changes: the copy is edited and approved as a new version.
    /// </summary>
    public static Result<InstrumentVersion> CreateDraftFrom(
        Instrument instrument, InstrumentVersion source, int versionNumber, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentNullException.ThrowIfNull(source);

        if (source.InstrumentId != instrument.Id)
        {
            return Result.Failure<InstrumentVersion>(QuestionnaireErrors.InstrumentMismatch);
        }

        var created = CreateDraft(instrument, versionNumber, now);
        if (created.IsFailure)
        {
            return created;
        }

        var draft = created.Value;
        draft.ContentReferenceAr = source.ContentReferenceAr;
        draft.ContentReferenceEn = source.ContentReferenceEn;
        foreach (var section in source._sections.OrderBy(section => section.DisplayOrder))
        {
            var sectionCopy = InstrumentSection.Create(draft.Id, section.Code, section.Title with { }, section.DisplayOrder);
            draft._sections.Add(sectionCopy);
            foreach (var item in section.Items.OrderBy(item => item.DisplayOrder))
            {
                var itemCopy = sectionCopy.AddItem(
                    item.Code, item.DisplayOrder, item.ItemType, item.IsRequired, item.ScoringCategory, item.ScoringDirection,
                    item.Prompt with { });
                foreach (var option in item.Options.OrderBy(option => option.DisplayOrder))
                {
                    itemCopy.AddOption(option.Code, option.DisplayOrder, option.Value, option.Label with { }, option.IsNonSubstantive);
                }
            }
        }

        return Result.Success(draft);
    }

    public Result SetContentReferences(string arabic, string english, DateTimeOffset now)
    {
        var editable = EnsureDraft(now);
        if (editable.IsFailure)
        {
            return editable;
        }

        if (!IsContentReference(arabic) || !IsContentReference(english))
        {
            return Result.Failure(QuestionnaireErrors.ContentReferenceInvalid);
        }

        ContentReferenceAr = arabic.Trim();
        ContentReferenceEn = english.Trim();
        UpdatedAt = now;
        return Result.Success();
    }

    public Result<InstrumentSection> AddSection(string code, LocalizedText title, int displayOrder, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(title);

        var editable = EnsureDraft(now);
        if (editable.IsFailure)
        {
            return Result.Failure<InstrumentSection>(editable.Error!);
        }

        if (!Codes.IsContentCode(code))
        {
            return Result.Failure<InstrumentSection>(QuestionnaireErrors.CodeInvalid);
        }

        if (_sections.Exists(section => section.Code == code))
        {
            return Result.Failure<InstrumentSection>(QuestionnaireErrors.SectionCodeTaken);
        }

        if (!IsDisplayOrder(displayOrder))
        {
            return Result.Failure<InstrumentSection>(QuestionnaireErrors.DisplayOrderInvalid);
        }

        if (_sections.Exists(section => section.DisplayOrder == displayOrder))
        {
            return Result.Failure<InstrumentSection>(QuestionnaireErrors.DisplayOrderTaken);
        }

        if (!title.FitsWithin(InstrumentSection.MaxTitleLength))
        {
            return Result.Failure<InstrumentSection>(QuestionnaireErrors.TextTooLong);
        }

        var added = InstrumentSection.Create(Id, code, title, displayOrder);
        _sections.Add(added);
        UpdatedAt = now;
        return Result.Success(added);
    }

    public Result<InstrumentItem> AddItem(
        InstrumentSectionId sectionId,
        string code,
        int displayOrder,
        ItemType itemType,
        bool isRequired,
        string? scoringCategory,
        ScoringDirection scoringDirection,
        LocalizedText prompt,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var editable = EnsureDraft(now);
        if (editable.IsFailure)
        {
            return Result.Failure<InstrumentItem>(editable.Error!);
        }

        var section = FindSection(sectionId);
        if (section is null)
        {
            return Result.Failure<InstrumentItem>(QuestionnaireErrors.SectionNotFound);
        }

        if (!Codes.IsContentCode(code))
        {
            return Result.Failure<InstrumentItem>(QuestionnaireErrors.CodeInvalid);
        }

        if (GetItems().Any(item => item.Code == code))
        {
            return Result.Failure<InstrumentItem>(QuestionnaireErrors.ItemCodeTaken);
        }

        if (!IsDisplayOrder(displayOrder))
        {
            return Result.Failure<InstrumentItem>(QuestionnaireErrors.DisplayOrderInvalid);
        }

        if (section.Items.Any(item => item.DisplayOrder == displayOrder))
        {
            return Result.Failure<InstrumentItem>(QuestionnaireErrors.DisplayOrderTaken);
        }

        if (!Enum.IsDefined(itemType))
        {
            return Result.Failure<InstrumentItem>(QuestionnaireErrors.ItemTypeInvalid);
        }

        if (!Enum.IsDefined(scoringDirection))
        {
            return Result.Failure<InstrumentItem>(QuestionnaireErrors.ScoringDirectionInvalid);
        }

        if (scoringCategory is not null && !Codes.IsSnakeCode(scoringCategory))
        {
            return Result.Failure<InstrumentItem>(QuestionnaireErrors.ScoringCategoryInvalid);
        }

        if (scoringDirection == ScoringDirection.NotScored && scoringCategory is not null)
        {
            return Result.Failure<InstrumentItem>(QuestionnaireErrors.NotScoredItemHasCategory);
        }

        if (!prompt.FitsWithin(InstrumentItem.MaxPromptLength))
        {
            return Result.Failure<InstrumentItem>(QuestionnaireErrors.TextTooLong);
        }

        var added = section.AddItem(code, displayOrder, itemType, isRequired, scoringCategory, scoringDirection, prompt);
        UpdatedAt = now;
        return Result.Success(added);
    }

    public Result<ResponseOption> AddOption(
        InstrumentItemId itemId, string code, int displayOrder, int value, LocalizedText label, DateTimeOffset now,
        bool isNonSubstantive = false)
    {
        ArgumentNullException.ThrowIfNull(label);

        var editable = EnsureDraft(now);
        if (editable.IsFailure)
        {
            return Result.Failure<ResponseOption>(editable.Error!);
        }

        var item = FindItem(itemId);
        if (item is null)
        {
            return Result.Failure<ResponseOption>(QuestionnaireErrors.ItemNotFound);
        }

        if (!Codes.IsContentCode(code))
        {
            return Result.Failure<ResponseOption>(QuestionnaireErrors.CodeInvalid);
        }

        if (item.Options.Any(option => option.Code == code))
        {
            return Result.Failure<ResponseOption>(QuestionnaireErrors.OptionCodeTaken);
        }

        if (!IsDisplayOrder(displayOrder))
        {
            return Result.Failure<ResponseOption>(QuestionnaireErrors.DisplayOrderInvalid);
        }

        if (item.Options.Any(option => option.DisplayOrder == displayOrder))
        {
            return Result.Failure<ResponseOption>(QuestionnaireErrors.DisplayOrderTaken);
        }

        if (value is < short.MinValue or > short.MaxValue)
        {
            return Result.Failure<ResponseOption>(QuestionnaireErrors.OptionValueInvalid);
        }

        if (item.Options.Any(option => option.Value == value))
        {
            return Result.Failure<ResponseOption>(QuestionnaireErrors.OptionValueTaken);
        }

        if (isNonSubstantive && item.Options.Any(option => option.IsNonSubstantive))
        {
            return Result.Failure<ResponseOption>(QuestionnaireErrors.NonSubstantiveOptionTaken);
        }

        if (!label.FitsWithin(ResponseOption.MaxLabelLength))
        {
            return Result.Failure<ResponseOption>(QuestionnaireErrors.TextTooLong);
        }

        var added = item.AddOption(code, displayOrder, value, label, isNonSubstantive);
        UpdatedAt = now;
        return Result.Success(added);
    }

    /// <summary>
    /// Publishes the draft. Besides the structure, every text of the version and its instrument must be bilingual,
    /// every single-choice item needs two substantive options, and a reverse-scored item needs a consecutive scale.
    /// </summary>
    public Result Approve(
        Instrument instrument, Approval approval, DateTimeOffset effectiveFrom, DateTimeOffset? effectiveTo, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentNullException.ThrowIfNull(approval);

        var editable = EnsureDraft(now);
        if (editable.IsFailure)
        {
            return editable;
        }

        if (instrument.Id != InstrumentId)
        {
            return Result.Failure(QuestionnaireErrors.InstrumentMismatch);
        }

        if (!UtcTime.IsUtc(effectiveFrom) || !UtcTime.IsUtc(effectiveTo))
        {
            return Result.Failure(CommonErrors.TimeNotUtc);
        }

        if (effectiveTo <= effectiveFrom)
        {
            return Result.Failure(QuestionnaireErrors.EffectiveWindowInvalid);
        }

        if (ContentReferenceAr is null || ContentReferenceEn is null)
        {
            return Result.Failure(QuestionnaireErrors.ContentReferencesMissing);
        }

        if (_sections.Count == 0)
        {
            return Result.Failure(QuestionnaireErrors.VersionHasNoSections);
        }

        if (_sections.Exists(section => section.Items.Count == 0))
        {
            return Result.Failure(QuestionnaireErrors.SectionHasNoItems);
        }

        if (GetItems().Any(item => item.ItemType == ItemType.SingleChoice && item.GetSubstantiveOptions().Count() < 2))
        {
            return Result.Failure(QuestionnaireErrors.ItemNeedsTwoOptions);
        }

        if (GetItems().Any(item => item.ScoringDirection == ScoringDirection.Reverse && !item.HasConsecutiveScale()))
        {
            return Result.Failure(QuestionnaireErrors.ReverseItemScaleInvalid);
        }

        if (!HasCompleteText(instrument))
        {
            return Result.Failure(QuestionnaireErrors.ArabicTextMissing);
        }

        Status = InstrumentVersionStatus.Approved;
        ApprovedBy = approval.ApprovedBy;
        ApprovedAt = approval.ApprovedAt;
        ApprovalSource = approval.Source;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        UpdatedAt = now;
        Raise(new InstrumentVersionApproved(Id, InstrumentId, now));
        return Result.Success();
    }

    /// <summary>
    /// Retires the version now, ending its effective window at <paramref name="effectiveTo"/>, which must be
    /// <paramref name="now"/> or earlier (and match an end already scheduled). From then on no new assignments use it;
    /// existing assignments, sessions and results keep pointing here. To end the window in the future, use
    /// <see cref="ScheduleEnd"/> and retire once that time has passed.
    /// </summary>
    public Result Retire(DateTimeOffset effectiveTo, DateTimeOffset now)
    {
        if (!UtcTime.IsUtc(now) || !UtcTime.IsUtc(effectiveTo))
        {
            return Result.Failure(CommonErrors.TimeNotUtc);
        }

        if (Status != InstrumentVersionStatus.Approved)
        {
            return Result.Failure(QuestionnaireErrors.VersionNotApproved);
        }

        if (effectiveTo > now)
        {
            return Result.Failure(QuestionnaireErrors.RetireInFuture);
        }

        if (EffectiveTo is { } existing && existing != effectiveTo)
        {
            return Result.Failure(QuestionnaireErrors.EffectiveToAlreadySet);
        }

        if (effectiveTo <= EffectiveFrom)
        {
            return Result.Failure(QuestionnaireErrors.EffectiveWindowInvalid);
        }

        Status = InstrumentVersionStatus.Retired;
        EffectiveTo = effectiveTo;
        RetiredAt = now;
        UpdatedAt = now;
        Raise(new InstrumentVersionRetired(Id, InstrumentId, now));
        return Result.Success();
    }

    /// <summary>
    /// Sets the end of an open effective window, usually in the future, so the next version can be approved from that
    /// time. The version stays Approved and effective until then; retire it once the end has passed.
    /// </summary>
    public Result ScheduleEnd(DateTimeOffset effectiveTo, DateTimeOffset now)
    {
        if (!UtcTime.IsUtc(now) || !UtcTime.IsUtc(effectiveTo))
        {
            return Result.Failure(CommonErrors.TimeNotUtc);
        }

        if (Status != InstrumentVersionStatus.Approved)
        {
            return Result.Failure(QuestionnaireErrors.VersionNotApproved);
        }

        if (EffectiveTo is not null)
        {
            return Result.Failure(QuestionnaireErrors.EffectiveToAlreadySet);
        }

        if (effectiveTo <= EffectiveFrom)
        {
            return Result.Failure(QuestionnaireErrors.EffectiveWindowInvalid);
        }

        EffectiveTo = effectiveTo;
        UpdatedAt = now;
        return Result.Success();
    }

    public bool IsEffectiveAt(DateTimeOffset time) =>
        Status == InstrumentVersionStatus.Approved
        && EffectiveFrom <= time
        && (EffectiveTo is null || time < EffectiveTo);

    public InstrumentSection? FindSection(InstrumentSectionId sectionId) =>
        _sections.Find(section => section.Id == sectionId);

    public InstrumentItem? FindItem(InstrumentItemId itemId) => GetItems().FirstOrDefault(item => item.Id == itemId);

    // A method rather than a property, so EF Core never tries to map it as a navigation.
    public IEnumerable<InstrumentItem> GetItems() => _sections.SelectMany(section => section.Items);

    /// <summary>
    /// Whether every text approval needs in both languages has its Arabic: the instrument's name and description, and
    /// every section title, item prompt and option label of this version. While it is false, <see cref="Approve"/>
    /// refuses with <see cref="QuestionnaireErrors.ArabicTextMissing"/>.
    /// </summary>
    public bool HasCompleteText(Instrument instrument)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        if (instrument.Id != InstrumentId)
        {
            throw new ArgumentException("The instrument is not this version's instrument.", nameof(instrument));
        }

        return instrument.Name.IsComplete
            && instrument.Description.IsComplete
            && _sections.TrueForAll(section => section.Title.IsComplete)
            && GetItems().All(item => item.Prompt.IsComplete && item.Options.All(option => option.Label.IsComplete));
    }

    private static bool IsDisplayOrder(int value) => value is >= 1 and <= short.MaxValue;

    private static bool IsContentReference(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= MaxContentReferenceLength;

    private Result EnsureDraft(DateTimeOffset now)
    {
        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure(CommonErrors.TimeNotUtc);
        }

        return Status == InstrumentVersionStatus.Draft
            ? Result.Success()
            : Result.Failure(QuestionnaireErrors.VersionNotDraft);
    }
}
