using FamilyResilience.Application.Analytics;
using FamilyResilience.Domain.Analytics;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.UnitTests.Application;

public sealed class ExportCatalogTests
{
    private static readonly DateTimeOffset October = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    private static ExportSessionRow Session(
        EmploymentStatus? employment = EmploymentStatus.SelfEmployed, string? section = "A", int nonSubstantive = 0) =>
        new(FamilyMemberId.New(), HouseholdId.New(), "test_instrument", 3, section, "wave_2026", SessionStatus.Scored, October,
            1990, 11, employment, true, 12, nonSubstantive);

    private static ExportScoreRow Score(decimal? total = 31.5m, string? tier = "band_low", string? incompleteReason = null, int excluded = 0) =>
        new(FamilyMemberId.New(), HouseholdId.New(), "test_instrument", 3, null, "wave_2026", "test_rules", 2,
            total is null ? ScoreStatus.Incomplete : ScoreStatus.Complete, incompleteReason, total, tier, excluded, October, 1960, 1, null);

    [Theory]
    [InlineData("assessment_sessions", ExportDataset.AssessmentSessions)]
    [InlineData("score_results", ExportDataset.ScoreResults)]
    public void Known_datasets_parse_and_print_back(string code, ExportDataset dataset)
    {
        Assert.Equal(dataset, ExportCatalog.ParseDataset(code).Value);
        Assert.Equal(code, ExportCatalog.CodeOf(dataset));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("answers")]
    [InlineData("Score_Results")]
    public void Other_datasets_are_refused(string? code) =>
        Assert.Equal(AnalyticsErrors.ExportDatasetInvalid, ExportCatalog.ParseDataset(code).Error);

    [Theory]
    [InlineData(ExportDataset.AssessmentSessions, "tier_code")]
    [InlineData(ExportDataset.ScoreResults, "answered_count")]
    [InlineData(ExportDataset.ScoreResults, "member_id")]
    [InlineData(ExportDataset.AssessmentSessions, "birth_year")]
    public void A_column_outside_the_datasets_allowlist_is_refused(ExportDataset dataset, string column) =>
        Assert.Equal(AnalyticsErrors.ExportColumnInvalid, ExportCatalog.CheckColumns(dataset, ["participant_pseudonym", column]).Error);

    [Fact]
    public void Allowed_columns_pass() =>
        Assert.True(ExportCatalog.CheckColumns(ExportDataset.ScoreResults, ["tier_code", "participant_pseudonym", "age_band"]).IsSuccess);

    [Theory]
    [InlineData(ExportDataset.AssessmentSessions)]
    [InlineData(ExportDataset.ScoreResults)]
    public void No_column_carries_a_direct_identifier_or_an_exact_quasi_identifier(ExportDataset dataset) =>
        Assert.All(ExportCatalog.ColumnsOf(dataset), column =>
        {
            Assert.DoesNotContain(column, new[] { "id", "member_id", "household_id", "session_id", "reference", "subject" });
            Assert.False(column.Contains("birth", StringComparison.Ordinal), column);
            Assert.False(column.EndsWith("_at", StringComparison.Ordinal), column);
        });

    [Fact]
    public void A_session_row_projects_coarsened_values_in_the_requested_order()
    {
        using var pseudonyms = new Pseudonymizer();
        var row = Session();

        var cells = ExportCatalog.Project(
            row,
            ["age_band", "submitted_month", "instrument_code", "version_number", "section_code", "wave_code", "session_status",
                "employment_status", "is_head_of_household", "answered_count", "participant_pseudonym", "household_pseudonym"],
            pseudonyms);

        Assert.Equal(
            ["30_44", "2026-10", "test_instrument", "3", "A", "wave_2026", "scored", "self_employed", "true", "12",
                pseudonyms.Member(row.MemberId), pseudonyms.Household(row.HouseholdId)],
            cells);
    }

    [Fact]
    public void Missing_values_project_as_null_or_not_recorded()
    {
        using var pseudonyms = new Pseudonymizer();

        Assert.Equal(
            ["not_recorded", null],
            ExportCatalog.Project(Session(employment: null, section: null), ["employment_status", "section_code"], pseudonyms));
        Assert.Equal(
            ["incomplete", null, null],
            ExportCatalog.Project(Score(total: null, tier: null), ["score_status", "total_score", "tier_code"], pseudonyms));
        Assert.Equal([null, "0"], ExportCatalog.Project(Score(), ["incomplete_reason", "excluded_count"], pseudonyms));
    }

    [Fact]
    public void A_score_row_projects_its_rule_set_score_and_month()
    {
        using var pseudonyms = new Pseudonymizer();

        Assert.Equal(
            ["test_rules", "2", "complete", "31.5", "band_low", "2026-10", "60_plus", "not_recorded"],
            ExportCatalog.Project(
                Score(),
                ["rule_set_code", "rule_set_version", "score_status", "total_score", "tier_code", "scored_month", "age_band", "employment_status"],
                pseudonyms));
    }

    [Theory]
    [InlineData(ExportDataset.AssessmentSessions)]
    [InlineData(ExportDataset.ScoreResults)]
    public void Every_listed_column_projects(ExportDataset dataset)
    {
        using var pseudonyms = new Pseudonymizer();
        var columns = ExportCatalog.ColumnsOf(dataset);

        var cells = dataset == ExportDataset.AssessmentSessions
            ? ExportCatalog.Project(Session(), columns, pseudonyms)
            : ExportCatalog.Project(Score(), columns, pseudonyms);

        Assert.Equal(columns.Count, cells.Count);
    }

    // total_score is numeric(9,3): PostgreSQL hands back 107.000, and a decimal prints the scale it carries.
    [Theory]
    [InlineData("107.000", "107")]
    [InlineData("31.500", "31.5")]
    [InlineData("32.125", "32.125")]
    [InlineData("14.670", "14.67")]
    [InlineData("0.000", "0")]
    [InlineData("100", "100")]
    public void A_total_score_is_written_without_trailing_zeros_whatever_the_culture(string stored, string exported)
    {
        using var pseudonyms = new Pseudonymizer();
        var total = decimal.Parse(stored, System.Globalization.CultureInfo.InvariantCulture);
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            // A culture that writes a decimal comma must not change the file.
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");

            Assert.Equal([exported], ExportCatalog.Project(Score(total: total), ["total_score"], pseudonyms));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }

    // A session answered entirely with 99 has 12 answers, and all 12 are "prefer not to answer".
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(12)]
    public void A_session_row_says_how_many_of_its_answers_were_non_substantive(int nonSubstantive)
    {
        using var pseudonyms = new Pseudonymizer();

        Assert.Equal(
            ["12", nonSubstantive.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            ExportCatalog.Project(Session(nonSubstantive: nonSubstantive), ["answered_count", "non_substantive_count"], pseudonyms));
    }

    [Fact]
    public void A_prorated_total_can_be_told_from_a_full_one_by_its_excluded_count()
    {
        using var pseudonyms = new Pseudonymizer();
        string[] columns = ["score_status", "incomplete_reason", "total_score", "excluded_count"];

        Assert.Equal(["complete", null, "33", "0"], ExportCatalog.Project(Score(total: 33.000m), columns, pseudonyms));
        Assert.Equal(["complete", null, "33", "3"], ExportCatalog.Project(Score(total: 33.000m, excluded: 3), columns, pseudonyms));
    }

    [Fact]
    public void An_incomplete_result_carries_its_reason_and_how_many_answers_were_excluded()
    {
        using var pseudonyms = new Pseudonymizer();

        Assert.Equal(
            ["incomplete", ScoreIncompleteReasons.NonSubstantiveShareExceeded, null, null, "5"],
            ExportCatalog.Project(
                Score(total: null, tier: null, incompleteReason: ScoreIncompleteReasons.NonSubstantiveShareExceeded, excluded: 5),
                ["score_status", "incomplete_reason", "total_score", "tier_code", "excluded_count"],
                pseudonyms));
    }

    // The allowlist is the whole contract: a new column is a deliberate change here (D-60, D-85).
    [Fact]
    public void The_catalog_offers_exactly_these_columns()
    {
        Assert.Equal(
            ["participant_pseudonym", "household_pseudonym", "instrument_code", "version_number", "section_code", "wave_code",
                "session_status", "submitted_month", "age_band", "employment_status", "is_head_of_household", "answered_count",
                "non_substantive_count"],
            ExportCatalog.ColumnsOf(ExportDataset.AssessmentSessions));
        Assert.Equal(
            ["participant_pseudonym", "household_pseudonym", "instrument_code", "version_number", "section_code", "wave_code",
                "rule_set_code", "rule_set_version", "score_status", "incomplete_reason", "total_score", "tier_code", "excluded_count",
                "scored_month", "age_band", "employment_status"],
            ExportCatalog.ColumnsOf(ExportDataset.ScoreResults));
    }

    [Theory]
    [InlineData(ExportDataset.AssessmentSessions)]
    [InlineData(ExportDataset.ScoreResults)]
    public void No_column_carries_an_answer_to_an_item(ExportDataset dataset) =>
        Assert.All(ExportCatalog.ColumnsOf(dataset), column =>
        {
            Assert.False(column.Contains("item", StringComparison.Ordinal), column);
            Assert.False(column.Contains("response", StringComparison.Ordinal), column);
            Assert.False(column.Contains("raw_value", StringComparison.Ordinal), column);
            Assert.False(column.Contains("option", StringComparison.Ordinal), column);
        });
}
