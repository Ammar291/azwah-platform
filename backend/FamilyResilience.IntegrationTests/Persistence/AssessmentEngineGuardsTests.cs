using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FamilyResilience.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class AssessmentEngineGuardsTests(DatabaseFixture fixture)
{
    private static FormattableString ApproveSql(Guid versionId) =>
        $"""
         UPDATE instrument_versions
         SET status = 'Approved', approved_by = 'raw-sql-test', approved_at = now(),
             approval_source = 'raw sql test', effective_from = now()
         WHERE id = {versionId}
         """;

    [Fact]
    public async Task Score_results_contributions_and_category_scores_refuse_updates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (score, _, _) = await fixture.ScoredSessionAsync(cancellationToken);
        var id = score.Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"UPDATE score_results SET computed_at = computed_at WHERE id = {id}", cancellationToken),
            PostgresErrorCodes.RestrictViolation);
        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"UPDATE score_contributions SET raw_value = raw_value WHERE score_result_id = {id}", cancellationToken),
            PostgresErrorCodes.RestrictViolation);
        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"UPDATE score_category_scores SET item_count = item_count WHERE score_result_id = {id}", cancellationToken),
            PostgresErrorCodes.RestrictViolation);
    }

    [Fact]
    public async Task A_version_with_text_missing_arabic_cannot_be_approved_even_by_raw_sql()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrument = TestData.NewInstrument();
        var draft = TestData.DraftVersion(instrument);
        var item = draft.Sections[0].Items[0];
        TestData.Ok(draft.AddOption(item.Id, "o4", 4, 4, TestData.DraftText("Option without Arabic"), TestData.Now));
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IInstrumentRepository>().Add(instrument);
                services.GetRequiredService<IInstrumentVersionRepository>().Add(draft);
                return Task.CompletedTask;
            },
            cancellationToken);

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(ApproveSql(draft.Id.Value), cancellationToken), PostgresErrorCodes.RestrictViolation);
    }

    [Theory]
    [InlineData("instrument name")]
    [InlineData("instrument description")]
    [InlineData("section title")]
    [InlineData("item prompt")]
    [InlineData("option label")]
    public async Task A_version_is_refused_raw_sql_approval_when_any_one_text_lacks_arabic(string missing)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        LocalizedText TextUnless(string place, string arabic, string english) =>
            place == missing ? TestData.DraftText(english) : TestData.Text(arabic, english);
        var instrument = TestData.Ok(Instrument.Create(
            TestData.UniqueCode("one_text_missing"),
            InstrumentKind.FamilyVoices,
            TextUnless("instrument name", "أداة", "Instrument"),
            TextUnless("instrument description", "وصف", "Description"),
            TestData.Now));
        var draft = TestData.Ok(InstrumentVersion.CreateDraft(instrument, 1, TestData.Now));
        TestData.Ok(draft.SetContentReferences("test-source (ar)", "test-source (en)", TestData.Now));
        var section = TestData.Ok(draft.AddSection("A", TextUnless("section title", "قسم", "Section"), 1, TestData.Now));
        var item = TestData.Ok(draft.AddItem(
            section.Id, "A1", 1, ItemType.SingleChoice, true, null, ScoringDirection.Forward,
            TextUnless("item prompt", "سؤال", "Question"), TestData.Now));
        TestData.Ok(draft.AddOption(item.Id, "o1", 1, 1, TextUnless("option label", "خيار", "Option"), TestData.Now));
        TestData.Ok(draft.AddOption(item.Id, "o2", 2, 2, TestData.Text("خيار", "Option"), TestData.Now));
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IInstrumentRepository>().Add(instrument);
                services.GetRequiredService<IInstrumentVersionRepository>().Add(draft);
                return Task.CompletedTask;
            },
            cancellationToken);

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(ApproveSql(draft.Id.Value), cancellationToken), PostgresErrorCodes.RestrictViolation);
    }

    [Fact]
    public async Task A_fully_bilingual_version_can_still_be_approved_by_raw_sql()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var draft = await fixture.DraftVersionAsync(cancellationToken);

        Assert.Equal(1, await fixture.ExecuteSqlAsync(ApproveSql(draft.Id.Value), cancellationToken));
    }
}
