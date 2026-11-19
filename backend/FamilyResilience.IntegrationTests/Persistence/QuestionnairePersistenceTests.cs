using System.Text.Json.Nodes;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FamilyResilience.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class QuestionnairePersistenceTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Approved_version_with_bilingual_content_round_trips_in_order()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await fixture.ApprovedVersionAsync(cancellationToken);

        var loaded = await fixture.InScopeAsync(
            services => services.GetRequiredService<IInstrumentVersionRepository>().GetAsync(version.Id, cancellationToken),
            cancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal(InstrumentVersionStatus.Approved, loaded.Status);
        Assert.Equal("approver-subject", loaded.ApprovedBy);
        Assert.Equal(TestData.Now.AddDays(-1), loaded.EffectiveFrom);
        Assert.Equal("test-source (ar)", loaded.ContentReferenceAr);
        Assert.Equal(new[] { "A", "B" }, loaded.Sections.OrderBy(s => s.DisplayOrder).Select(s => s.Code));
        var item = loaded.Sections.Single(s => s.Code == "A").Items.Single(i => i.Code == "A2");
        Assert.Equal("سؤال", item.Prompt.Arabic);
        Assert.Equal("Question", item.Prompt.English);
        Assert.Equal(ScoringDirection.Forward, item.ScoringDirection);
        Assert.Equal(new[] { 1, 2, 3 }, item.Options.OrderBy(o => o.DisplayOrder).Select(o => o.Value));
        Assert.Equal("خيار", item.Options[0].Label.Arabic);
    }

    [Fact]
    public async Task Instrument_is_found_by_code()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await fixture.ApprovedVersionAsync(cancellationToken);
        var instrument = await fixture.InScopeAsync(
            services => services.GetRequiredService<IInstrumentRepository>().GetAsync(version.InstrumentId, cancellationToken),
            cancellationToken);
        Assert.NotNull(instrument);

        var byCode = await fixture.InScopeAsync(
            services => services.GetRequiredService<IInstrumentRepository>().GetByCodeAsync(instrument.Code, cancellationToken),
            cancellationToken);

        Assert.Equal(instrument.Id, byCode?.Id);
        Assert.Equal("أداة", byCode?.Name.Arabic);
    }

    [Fact]
    public async Task A_duplicate_instrument_code_returns_instrument_code_taken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = TestData.NewInstrument();
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IInstrumentRepository>().Add(first);
                return Task.CompletedTask;
            },
            cancellationToken);
        var duplicate = TestData.Ok(Instrument.Create(
            first.Code, InstrumentKind.FamilyVoices, TestData.Text("أداة", "Instrument"), TestData.Text("وصف", "Description"), TestData.Now));

        await using var scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().Add(duplicate);
        var result = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);

        Assert.Equal(QuestionnaireErrors.InstrumentCodeTaken, result.Error);
    }

    [Fact]
    public async Task A_duplicate_version_number_returns_version_number_taken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var draft = await fixture.DraftVersionAsync(cancellationToken);
        var instrument = await fixture.InScopeAsync(
            services => services.GetRequiredService<IInstrumentRepository>().GetAsync(draft.InstrumentId, cancellationToken),
            cancellationToken);
        Assert.NotNull(instrument);

        await using var scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<IInstrumentVersionRepository>()
            .Add(TestData.Ok(InstrumentVersion.CreateDraft(instrument, draft.VersionNumber, TestData.Now)));
        var result = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);

        Assert.Equal(QuestionnaireErrors.VersionNumberTaken, result.Error);
    }

    [Fact]
    public async Task Effective_approved_version_is_found_by_time()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await fixture.ApprovedVersionAsync(
            cancellationToken, effectiveFrom: TestData.Now, effectiveTo: TestData.Now.AddDays(30));

        var found = await fixture.InScopeAsync(
            services => services.GetRequiredService<IInstrumentVersionRepository>()
                .GetEffectiveApprovedAsync(version.InstrumentId, TestData.Now.AddDays(1), cancellationToken),
            cancellationToken);
        var tooEarly = await fixture.InScopeAsync(
            services => services.GetRequiredService<IInstrumentVersionRepository>()
                .GetEffectiveApprovedAsync(version.InstrumentId, TestData.Now.AddDays(-1), cancellationToken),
            cancellationToken);

        Assert.Equal(version.Id, found?.Id);
        Assert.Equal(4, found?.GetItems().Count());
        Assert.Null(tooEarly);
    }

    [Fact]
    public async Task Rule_set_definition_round_trips_as_equivalent_json_and_only_approved_sets_are_listed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await fixture.ApprovedVersionAsync(cancellationToken);
        var approved = await fixture.ApprovedRuleSetAsync(RuleSetKind.Scoring, version, cancellationToken);
        var proposed = TestData.Ok(RuleSet.Propose(
            RuleSetKind.Scoring, TestData.UniqueCode("proposed"), 1, version, """{"b": [1, 2], "a": {"c": true}}""", TestData.Now));
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IRuleSetRepository>().Add(proposed);
                return Task.CompletedTask;
            },
            cancellationToken);

        var loaded = await fixture.InScopeAsync(
            services => services.GetRequiredService<IRuleSetRepository>().GetAsync(proposed.Id, cancellationToken),
            cancellationToken);
        var listed = await fixture.InScopeAsync(
            services => services.GetRequiredService<IRuleSetRepository>().ListApprovedAsync(RuleSetKind.Scoring, version.Id, cancellationToken),
            cancellationToken);

        Assert.NotNull(loaded);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(proposed.Definition), JsonNode.Parse(loaded.Definition)));
        Assert.Equal(approved.Id, Assert.Single(listed).Id);
    }

    [Fact]
    public async Task Database_rejects_an_item_whose_section_belongs_to_another_version()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = await fixture.DraftVersionAsync(cancellationToken);
        var second = await fixture.DraftVersionAsync(cancellationToken);
        var itemId = first.Sections[0].Items[0].Id.Value;
        var otherVersionId = second.Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"UPDATE instrument_items SET instrument_version_id = {otherVersionId}, code = 'moved' WHERE id = {itemId}", cancellationToken),
            PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task Database_rejects_an_approved_version_without_approval_details()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var draftId = (await fixture.DraftVersionAsync(cancellationToken)).Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"UPDATE instrument_versions SET status = 'Approved' WHERE id = {draftId}", cancellationToken),
            PostgresErrorCodes.CheckViolation,
            "ck_instrument_versions_approval");
    }

    [Fact]
    public async Task Database_rejects_a_rule_set_definition_that_is_not_an_object()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await fixture.ApprovedVersionAsync(cancellationToken);
        var proposed = TestData.Ok(RuleSet.Propose(RuleSetKind.Scoring, TestData.UniqueCode("rules"), 1, version, "{}", TestData.Now));
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IRuleSetRepository>().Add(proposed);
                return Task.CompletedTask;
            },
            cancellationToken);
        var ruleSetId = proposed.Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"UPDATE rule_sets SET definition = '[1, 2]'::jsonb WHERE id = {ruleSetId}", cancellationToken),
            PostgresErrorCodes.CheckViolation,
            "ck_rule_sets_definition_is_object");
    }

    [Fact]
    public async Task Draft_content_keeps_missing_arabic_non_substantive_options_and_unspecified_directions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrument = TestData.Ok(Instrument.Create(
            TestData.UniqueCode("draft_text"), InstrumentKind.FamilyVoices, TestData.DraftText("Instrument"),
            TestData.DraftText("Description"), TestData.Now));
        var version = TestData.Ok(InstrumentVersion.CreateDraft(instrument, 1, TestData.Now));
        var section = TestData.Ok(version.AddSection("A", TestData.DraftText("Module A"), 1, TestData.Now));
        var item = TestData.Ok(version.AddItem(
            section.Id, "Q04", 1, ItemType.SingleChoice, true, null, ScoringDirection.Unspecified,
            TestData.Text("سؤال", "Question"), TestData.Now));
        TestData.Ok(version.AddOption(item.Id, "1", 1, 1, TestData.DraftText("Extremely Difficult"), TestData.Now));
        TestData.Ok(version.AddOption(
            item.Id, "99", 2, 99, TestData.Text("يفضل عدم الإجابة", "Prefer not to answer"), TestData.Now, isNonSubstantive: true));
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IInstrumentRepository>().Add(instrument);
                services.GetRequiredService<IInstrumentVersionRepository>().Add(version);
                return Task.CompletedTask;
            },
            cancellationToken);

        var saved = await fixture.QueryAsync(db => db.InstrumentVersions
            .Include(candidate => candidate.Sections).ThenInclude(candidate => candidate.Items).ThenInclude(candidate => candidate.Options)
            .SingleAsync(candidate => candidate.Id == version.Id, cancellationToken));
        var savedInstrument = await fixture.QueryAsync(db =>
            db.Instruments.SingleAsync(candidate => candidate.Id == instrument.Id, cancellationToken));

        var savedItem = Assert.Single(saved.GetItems());
        Assert.Equal(ScoringDirection.Unspecified, savedItem.ScoringDirection);
        Assert.Null(saved.Sections[0].Title.Arabic);
        var options = savedItem.Options.OrderBy(option => option.DisplayOrder).ToList();
        Assert.Null(options[0].Label.Arabic);
        Assert.False(options[0].IsNonSubstantive);
        Assert.True(options[1].IsNonSubstantive);
        Assert.Equal(99, options[1].Value);
        Assert.Null(savedInstrument.Name.Arabic);
        Assert.False(savedInstrument.Description.IsComplete);
    }
}
