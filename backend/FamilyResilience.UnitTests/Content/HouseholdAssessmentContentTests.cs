using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Infrastructure.Content;
using FamilyResilience.UnitTests.Support;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.SourceContent;

/// <summary>
/// The committed household-assessment draft is the earlier investor demo's 39-item instrument, copied verbatim. Nothing in
/// it is invented, and nothing in it is approved (open questions 1 and 2). The demo is not in this repository (D-150):
/// the content was compared with it where it is kept, and the fingerprints below fail if the copied text changes after
/// that check.
/// </summary>
public sealed class HouseholdAssessmentContentTests
{
    /// <summary>The demo's seven dimensions, in order, with their names in both languages and their item counts.</summary>
    private static readonly (string Code, string En, string Ar, int Items)[] DemoDimensions =
    [
        ("communication", "Family Communication & Problem Solving", "التواصل الأسري وحل المشكلات", 7),
        ("connectedness", "Family Connectedness & Cohesion", "الترابط والتماسك الأسري", 5),
        ("outlook", "Positive Outlook & Shared Values", "النظرة الإيجابية والقيم المشتركة", 5),
        ("extended_family", "Extended Family Support Networks", "شبكات دعم العائلة الممتدة", 5),
        ("wealth_debt", "Intergenerational Wealth & Debt Management", "إدارة الثروة والديون عبر الأجيال", 6),
        ("seniors", "Empowerment & Integration of Seniors", "تمكين كبار السن ودمجهم", 5),
        ("work_life", "Work–Life Balance & Caregiving", "التوازن بين العمل والحياة والرعاية", 6),
    ];

    /// <summary>The statements the demo marks reverse-scored.</summary>
    private static readonly string[] ReverseScored = ["Q04", "Q07", "Q11", "Q16", "Q21", "Q25", "Q26", "Q32", "Q36", "Q38"];

    private static readonly string[] EnglishLabels = ["Strongly Disagree", "Disagree", "Agree", "Strongly Agree"];
    private static readonly string[] ArabicLabels = ["لا أوافق بشدة", "لا أوافق", "أوافق", "أوافق بشدة"];

    private static readonly InstrumentContentPackage Package = ContentFiles.ReadPackage(ContentFiles.HouseholdAssessment);

    private static List<ContentItemDto> Items => Package.Version.Sections.SelectMany(section => section.Items).ToList();

    [Fact]
    public void Every_demo_statement_is_an_item_in_demo_order_numbered_q01_to_q39() =>
        Assert.Equal(Enumerable.Range(1, 39).Select(number => $"Q{number:00}"), Items.Select(item => item.Code));

    [Fact]
    public void Sections_are_the_demo_dimensions_in_order_named_in_both_languages() =>
        Assert.Equal(
            DemoDimensions.Select((dimension, index) => (dimension.Code, index + 1, dimension.En, (string?)dimension.Ar, dimension.Items)),
            Package.Version.Sections.Select(section => (section.Code, section.DisplayOrder, section.Title.En, section.Title.Ar, section.Items.Count)));

    [Fact]
    public void Each_item_carries_its_sections_dimension_as_scoring_category() =>
        Assert.All(Package.Version.Sections, section =>
            Assert.All(section.Items, item => Assert.Equal(section.Code, item.ScoringCategory)));

    [Fact]
    public void Display_orders_run_from_one_in_array_order_within_each_section() =>
        Assert.All(Package.Version.Sections, section =>
            Assert.Equal(Enumerable.Range(1, section.Items.Count), section.Items.Select(item => item.DisplayOrder)));

    [Fact]
    public void Statement_text_in_both_languages_is_unchanged_since_it_was_checked_against_the_demo() =>
        Assert.Equal("6fa4a3960b5c2468e0028a4bc8b20c2eaded5432335b52d675ad761402fc434e", ContentFingerprint.OfPrompts(Items));

    [Fact]
    public void Answer_labels_are_unchanged_since_they_were_checked_against_the_demo() =>
        Assert.Equal("04ccc1dd0f031f3f2eb3df1a2179e8b0df1d5e5e1fd3daaa78df9c91842d2028", ContentFingerprint.OfOptions(Items));

    [Fact]
    public void Reverse_scored_statements_are_marked_reverse_and_the_rest_forward() =>
        Assert.Equal(
            Items.Select(item => ReverseScored.Contains(item.Code) ? ScoringDirection.Reverse : ScoringDirection.Forward),
            Items.Select(item => item.ScoringDirection));

    [Fact]
    public void Every_item_is_a_required_single_choice_with_the_four_agreement_options() =>
        Assert.All(Items, item =>
        {
            Assert.Equal(ItemType.SingleChoice, item.ItemType);
            Assert.True(item.IsRequired);
            Assert.Equal(new[] { "1", "2", "3", "4" }, item.Options.Select(option => option.Code));
            Assert.Equal(new[] { 1, 2, 3, 4 }, item.Options.Select(option => option.Value));
            Assert.Equal(new[] { 1, 2, 3, 4 }, item.Options.Select(option => option.DisplayOrder));
            Assert.Equal(EnglishLabels, item.Options.Select(option => option.Label.En));
            Assert.Equal(ArabicLabels, item.Options.Select(option => option.Label.Ar));
            Assert.All(item.Options, option => Assert.False(option.IsNonSubstantive));
        });

    [Fact]
    public void The_instrument_is_the_household_assessment_named_and_described_as_in_the_demo()
    {
        Assert.Equal("household_assessment", Package.Instrument.Code);
        Assert.Equal(InstrumentKind.HouseholdAssessment, Package.Instrument.Kind);
        Assert.Equal(("تقييم المرونة الأسرية (39 عبارة)", "Family Resilience Assessment (39 items)"), (Package.Instrument.Name.Ar, Package.Instrument.Name.En));
        Assert.Equal(
            ("39 عبارة تغطي سبعة مجالات من الحياة الأسرية.", "39 statements across seven areas of family life."),
            (Package.Instrument.Description.Ar, Package.Instrument.Description.En));
        Assert.Contains("investor demo", Package.Version.ContentReferenceEn, StringComparison.Ordinal);
        Assert.Contains("not approved", Package.Version.ContentReferenceAr, StringComparison.Ordinal);
    }

    [Fact]
    public void The_content_imports_as_a_draft_of_39_items_in_7_sections()
    {
        var instrument = Ok(InstrumentContentImporter.CreateInstrument(Package.Instrument, Now));
        var draft = Ok(InstrumentContentImporter.BuildDraft(instrument, 1, Package.Version, Now));

        Assert.Equal(InstrumentVersionStatus.Draft, draft.Status);
        Assert.Equal(7, draft.Sections.Count);
        Assert.Equal(39, draft.GetItems().Count());
    }
}
