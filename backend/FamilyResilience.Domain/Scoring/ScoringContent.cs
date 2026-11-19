using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Domain.Scoring;

/// <summary>
/// The items in one scoring scope (a module, or the whole version), copied out of the approved version so the
/// calculation never reads the database.
/// </summary>
public sealed record ScoringContent(IReadOnlyList<ScoringItem> Items)
{
    public IReadOnlyList<ScoringItem> ScoredItems() => Items.Where(item => item.IsScored).ToList();

    public int MinimumTotal() => ScoredItems().Sum(item => item.ScaleMinimum());

    public int MaximumTotal() => ScoredItems().Sum(item => item.ScaleMaximum());

    public static Result<ScoringContent> From(InstrumentVersion version, string? sectionCode)
    {
        ArgumentNullException.ThrowIfNull(version);

        var sections = version.Sections
            .Where(section => sectionCode is null || section.Code == sectionCode)
            .OrderBy(section => section.DisplayOrder)
            .ToList();
        if (sectionCode is not null && sections.Count == 0)
        {
            return Result.Failure<ScoringContent>(QuestionnaireErrors.SectionNotFound);
        }

        var items = sections
            .SelectMany(section => section.Items.OrderBy(item => item.DisplayOrder))
            .Select(item => new ScoringItem(
                item.Id,
                item.Code,
                item.ScoringCategory,
                item.ScoringDirection,
                item.Options
                    .OrderBy(option => option.DisplayOrder)
                    .Select(option => new ScoringOption(option.Id, option.Value, option.IsNonSubstantive))
                    .ToList())
            {
                IsRequired = item.IsRequired,
            })
            .ToList();
        return Result.Success(new ScoringContent(items));
    }
}
