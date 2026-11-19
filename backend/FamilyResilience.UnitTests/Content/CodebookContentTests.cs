using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Infrastructure.Content;
using FamilyResilience.UnitTests.Support;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.SourceContent;

/// <summary>
/// The committed draft content is the codebook, copied verbatim. Nothing in it is invented. The codebook is a
/// third-party document and is not in this repository (D-150): the content was compared with it line by line where it
/// is kept, and the fingerprints below fail if the copied text changes after that check.
/// </summary>
public sealed class CodebookContentTests
{
    /// <summary>Exact English label → Arabic, as printed in the earlier sample questions document (not included).</summary>
    private static readonly Dictionary<string, string> ReusedArabic = new(StringComparer.Ordinal)
    {
        ["Extremely Difficult"] = "صعب للغاية",
        ["Difficult"] = "صعب",
        ["Neutral"] = "محايد",
        ["Easy"] = "سهل",
        ["Extremely Easy"] = "سهل للغاية",
        ["No Burden"] = "لا تشكل أي عبء",
        ["Low Burden"] = "عبء خفيف",
        ["Moderate Burden"] = "عبء متوسط",
        ["High Burden"] = "عبء ثقيل",
        ["Severe Burden"] = "عبء شديد للغاية",
        ["Never"] = "أبداً",
        ["Rarely"] = "نادراً",
        ["Sometimes"] = "أحياناً",
        ["Often"] = "غالباً",
        ["Always"] = "دائماً",
        ["Strongly Disagree"] = "لا أوافق بشدة",
        ["Disagree"] = "لا أوافق",
        ["Agree"] = "أوافق",
        ["Strongly Agree"] = "أوافق بشدة",
        ["Rarely/Never"] = "نادراً / أبداً",
        ["A few times a month"] = "مرات قليلة في الشهر",
        ["Once a week"] = "مرة في الأسبوع",
        ["A few times a week"] = "مرات قليلة في الأسبوع",
        ["Daily"] = "يومياً",
        ["Seldom"] = "نادراً ما أفعل",
        ["Frequently"] = "غالباً",
        ["Every day"] = "كل يوم",
    };

    /// <summary>The codebook's module questions, by module, in codebook order (its roster questions Q01 to Q03 are not items).</summary>
    private static readonly (string Module, string[] Questions)[] CodebookModules =
    [
        ("A", [
            "Q04_Financial_Cushion", "Q05_Debt_Service_Burden", "Q06_Monthly_Savings", "Q07_Healthcare_Costs",
            "Q08_Longterm_Planning", "Q09_Emergency_Liquidity", "Q10_Essential_Costs_Inflation", "Q11_Housing_Maintenance",
            "Q12_Financial_Literacy", "Q13_Subsidy_Reliance",
        ]),
        ("B", [
            "Q14_Workplace_Exhaustion", "Q15_Work_Family_Spillover", "Q16_Developmental_Time", "Q17_Workplace_Flexibility",
            "Q18_Parenting_Efficacy", "Q19_Coparenting_Strain", "Q20_Weekend_Quality_Time", "Q21_Guilt_Anxiety",
            "Q22_Childcare_Support_Trust", "Q23_Caregiving_Overlap", "Q24_Overtime_Demands", "Q25_Physical_Toll",
            "Q26_Leisure_Deprivation",
        ]),
        ("C", [
            "Q27_Household_Integration", "Q28_Social_Connectedness", "Q29_Grandparenting_Cushion", "Q30_Mobility_Independence",
            "Q31_IADL_Independence", "Q32_Digital_Inclusion", "Q33_Healthcare_Access", "Q34_Mental_Wellness",
            "Q35_Community_Infrastructure", "Q36_Purpose_Civic_Fulfillment", "Q37_Cognitive_Stimulation",
            "Q38_Caregiver_Relationship", "Q39_Physical_Resilience_Falls",
        ]),
    ];

    private static readonly InstrumentContentPackage Package = ContentFiles.ReadPackage(ContentFiles.AbuDhabiFamilySurvey);

    private static List<ContentItemDto> Items => Package.Version.Sections.SelectMany(section => section.Items).ToList();

    [Fact]
    public void Every_module_question_of_the_codebook_is_an_item_of_its_module_in_codebook_order()
    {
        Assert.Equal(
            CodebookModules.Select(module => (module.Module, string.Join(',', module.Questions))),
            Package.Version.Sections.Select(section => (section.Code, string.Join(',', section.Items.Select(item => item.Code)))));
        Assert.DoesNotContain(Items, item => item.Code is "Q01_Household_Count" or "Q02_Member_Breakdown" or "Q03_Primary_Caregiver");
    }

    [Fact]
    public void Display_orders_run_from_one_in_array_order_within_each_section() =>
        Assert.All(Package.Version.Sections, section =>
            Assert.Equal(Enumerable.Range(1, section.Items.Count), section.Items.Select(item => item.DisplayOrder)));

    [Fact]
    public void Sections_are_the_codebook_modules_a_b_and_c() =>
        Assert.Equal(
            new[] { ("A", 1, "Module A: Finance", (string?)null, 10), ("B", 2, "Module B: Parenting & Burnout", null, 13), ("C", 3, "Module C: Active Aging", null, 13) },
            Package.Version.Sections.Select(section => (section.Code, section.DisplayOrder, section.Title.En, section.Title.Ar, section.Items.Count)));

    [Fact]
    public void Question_text_in_both_languages_is_unchanged_since_it_was_checked_against_the_codebook() =>
        Assert.Equal("1d32c97d89b7aa4682aa7a0632f157e973702c46d2a8527a398027e775c145a4", ContentFingerprint.OfPrompts(Items));

    [Fact]
    public void Answer_labels_are_unchanged_since_they_were_checked_against_the_codebook() =>
        Assert.Equal("973b1ac0943fc9aad10a7f1fbc3b2a0307d390533a5abb9c837561d5a01641c0", ContentFingerprint.OfOptions(Items));

    [Fact]
    public void Options_are_one_to_five_then_a_non_substantive_99() =>
        Assert.All(Items, item =>
        {
            Assert.Equal(new[] { "1", "2", "3", "4", "5", "99" }, item.Options.Select(option => option.Code));
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 99 }, item.Options.Select(option => option.Value));
            Assert.Equal(Enumerable.Range(1, 6), item.Options.Select(option => option.DisplayOrder));
            Assert.All(item.Options.Take(5), option => Assert.False(string.IsNullOrWhiteSpace(option.Label.En)));
            Assert.All(item.Options.Take(5), option => Assert.False(option.IsNonSubstantive));
            Assert.True(item.Options[5].IsNonSubstantive);
            Assert.Equal(("يفضل عدم الإجابة", "Prefer not to answer"), (item.Options[5].Label.Ar, item.Options[5].Label.En));
        });

    [Fact]
    public void Arabic_answer_labels_are_reused_only_where_the_earlier_source_has_the_same_english_label()
    {
        foreach (var option in Items.SelectMany(item => item.Options.Take(5)))
        {
            Assert.Equal(ReusedArabic.GetValueOrDefault(option.Label.En), option.Label.Ar);
        }
    }

    [Fact]
    public void No_item_has_a_scoring_direction_or_category_yet_and_every_item_is_required() =>
        Assert.All(Items, item =>
        {
            Assert.Equal(ItemType.SingleChoice, item.ItemType);
            Assert.Equal(ScoringDirection.Unspecified, item.ScoringDirection);
            Assert.Null(item.ScoringCategory);
            Assert.True(item.IsRequired);
        });

    [Fact]
    public void The_instrument_is_the_family_voices_survey_whose_arabic_names_are_still_missing()
    {
        Assert.Equal("abu_dhabi_family_survey", Package.Instrument.Code);
        Assert.Equal(InstrumentKind.FamilyVoices, Package.Instrument.Kind);
        Assert.Equal("Abu Dhabi Family Survey", Package.Instrument.Name.En);
        Assert.Null(Package.Instrument.Name.Ar);
        Assert.Null(Package.Instrument.Description.Ar);
    }

    [Fact]
    public void The_arabic_typos_in_the_source_are_kept_verbatim_for_the_content_owner_to_fix()
    {
        Assert.Contains(" in ", Items.Single(item => item.Code == "Q19_Coparenting_Strain").Prompt.Ar, StringComparison.Ordinal);
        Assert.Contains("المتمتطلبات", Items.Single(item => item.Code == "Q23_Caregiving_Overlap").Prompt.Ar, StringComparison.Ordinal);
    }

    [Fact]
    public void The_content_imports_as_a_draft_that_cannot_be_published_until_its_arabic_is_complete()
    {
        var instrument = Ok(InstrumentContentImporter.CreateInstrument(Package.Instrument, Now));
        var draft = Ok(InstrumentContentImporter.BuildDraft(instrument, 1, Package.Version, Now));

        Assert.Equal(36, draft.GetItems().Count());
        Assert.Equal(QuestionnaireErrors.ArabicTextMissing, draft.Approve(instrument, TestApproval(), Now, null, Now).Error);
    }

    [Fact]
    public void The_imported_draft_carries_every_section_item_and_option_of_the_content_file_unchanged()
    {
        var instrument = Ok(InstrumentContentImporter.CreateInstrument(Package.Instrument, Now));
        var draft = Ok(InstrumentContentImporter.BuildDraft(instrument, 1, Package.Version, Now));

        Assert.Equal(Package.Version.Sections.Count, draft.Sections.Count);
        foreach (var sectionDto in Package.Version.Sections)
        {
            var section = draft.Sections.Single(candidate => candidate.Code == sectionDto.Code);
            Assert.Equal(sectionDto.DisplayOrder, section.DisplayOrder);
            Assert.Equal(sectionDto.Title.En, section.Title.English);
            Assert.Equal(sectionDto.Title.Ar, section.Title.Arabic);
            Assert.Equal(sectionDto.Items.Select(item => item.Code), section.Items.OrderBy(item => item.DisplayOrder).Select(item => item.Code));

            foreach (var itemDto in sectionDto.Items)
            {
                var item = section.Items.Single(candidate => candidate.Code == itemDto.Code);
                Assert.Equal(itemDto.DisplayOrder, item.DisplayOrder);
                Assert.Equal(itemDto.ItemType, item.ItemType);
                Assert.Equal(itemDto.IsRequired, item.IsRequired);
                Assert.Equal(itemDto.ScoringCategory, item.ScoringCategory);
                Assert.Equal(itemDto.ScoringDirection, item.ScoringDirection);
                Assert.Equal(itemDto.Prompt.En, item.Prompt.English);
                Assert.Equal(itemDto.Prompt.Ar, item.Prompt.Arabic);
                Assert.Equal(itemDto.Options.Count, item.Options.Count);

                foreach (var optionDto in itemDto.Options)
                {
                    var option = item.Options.Single(candidate => candidate.Code == optionDto.Code);
                    Assert.Equal(optionDto.DisplayOrder, option.DisplayOrder);
                    Assert.Equal(optionDto.Value, option.Value);
                    Assert.Equal(optionDto.IsNonSubstantive, option.IsNonSubstantive);
                    Assert.Equal(optionDto.Label.En, option.Label.English);
                    Assert.Equal(optionDto.Label.Ar, option.Label.Arabic);
                }
            }
        }
    }
}
