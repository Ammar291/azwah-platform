using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.Questionnaires;

public sealed class InstrumentVersionTests
{
    [Fact]
    public void Draft_content_is_kept_in_order_with_bilingual_text()
    {
        var version = DraftVersion();

        Assert.Equal(InstrumentVersionStatus.Draft, version.Status);
        Assert.Equal(new[] { "A", "B" }, version.Sections.Select(section => section.Code));
        var item = version.Sections[0].Items[1];
        Assert.Equal("A2", item.Code);
        Assert.Equal(version.Sections[0].Id, item.InstrumentSectionId);
        Assert.Equal(version.Id, item.InstrumentVersionId);
        Assert.Equal(new[] { 1, 2, 3 }, item.Options.Select(option => option.Value));
        Assert.Equal("سؤال", item.Prompt.Arabic);
        Assert.Equal(4, version.GetItems().Count());
    }

    [Fact]
    public void Section_codes_and_display_orders_are_unique_in_a_version()
    {
        var version = DraftVersion();

        Assert.Equal(QuestionnaireErrors.SectionCodeTaken, version.AddSection("A", Text("قسم", "Section"), 9, Now).Error);
        Assert.Equal(QuestionnaireErrors.DisplayOrderTaken, version.AddSection("C", Text("قسم", "Section"), 1, Now).Error);
    }

    [Fact]
    public void Item_codes_are_unique_across_the_version()
    {
        var version = DraftVersion();
        var sectionB = version.Sections[1];

        Assert.Equal(
            QuestionnaireErrors.ItemCodeTaken,
            version.AddItem(sectionB.Id, "A1", 9, ItemType.SingleChoice, true, null, ScoringDirection.Forward, Text("سؤال", "Question"), Now).Error);
    }

    [Fact]
    public void Option_codes_values_and_orders_are_unique_in_an_item()
    {
        var version = DraftVersion();
        var item = version.Sections[0].Items[0];

        Assert.Equal(QuestionnaireErrors.OptionCodeTaken, version.AddOption(item.Id, "o1", 9, 9, Text("خيار", "Option"), Now).Error);
        Assert.Equal(QuestionnaireErrors.OptionValueTaken, version.AddOption(item.Id, "o9", 9, 1, Text("خيار", "Option"), Now).Error);
        Assert.Equal(QuestionnaireErrors.DisplayOrderTaken, version.AddOption(item.Id, "o9", 1, 9, Text("خيار", "Option"), Now).Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(40000)]
    public void Display_order_must_be_a_positive_smallint(int displayOrder) =>
        Assert.Equal(
            QuestionnaireErrors.DisplayOrderInvalid,
            DraftVersion().AddSection("C", Text("قسم", "Section"), displayOrder, Now).Error);

    [Fact]
    public void Adding_an_item_to_an_unknown_section_is_rejected() =>
        Assert.Equal(
            QuestionnaireErrors.SectionNotFound,
            DraftVersion().AddItem(InstrumentSectionId.New(), "X1", 1, ItemType.SingleChoice, true, null, ScoringDirection.Forward, Text("سؤال", "Question"), Now).Error);

    [Fact]
    public void Scoring_category_must_be_a_snake_code()
    {
        var version = DraftVersion();

        Assert.Equal(
            QuestionnaireErrors.ScoringCategoryInvalid,
            version.AddItem(version.Sections[0].Id, "X1", 9, ItemType.SingleChoice, true, "Family Communication", ScoringDirection.Forward, Text("سؤال", "Question"), Now).Error);
    }

    [Fact]
    public void Approval_records_the_approval_and_the_effective_window_and_raises_an_event()
    {
        var version = DraftVersion();

        Assert.True(ApproveVersion(version, Now, Now.AddYears(1)).IsSuccess);

        Assert.Equal(InstrumentVersionStatus.Approved, version.Status);
        Assert.Equal("approver-subject", version.ApprovedBy);
        Assert.Equal(Now, version.EffectiveFrom);
        Assert.Equal(Now.AddYears(1), version.EffectiveTo);
        Assert.IsType<InstrumentVersionApproved>(Assert.Single(version.GetDomainEvents()));
    }

    [Fact]
    public void Approved_content_cannot_change()
    {
        var version = ApprovedVersion();
        var item = version.Sections[0].Items[0];

        Assert.Equal(QuestionnaireErrors.VersionNotDraft, version.AddSection("C", Text("قسم", "Section"), 3, Now).Error);
        Assert.Equal(QuestionnaireErrors.VersionNotDraft, version.AddOption(item.Id, "o9", 9, 9, Text("خيار", "Option"), Now).Error);
        Assert.Equal(QuestionnaireErrors.VersionNotDraft, version.SetContentReferences("x", "y", Now).Error);
        Assert.Equal(QuestionnaireErrors.VersionNotDraft, ApproveVersion(version, Now, null).Error);
    }

    [Fact]
    public void Approval_needs_content_references()
    {
        var version = Ok(InstrumentVersion.CreateDraft(NewInstrument(), 1, Now));
        var section = Ok(version.AddSection("A", Text("قسم", "Section"), 1, Now));
        var item = Ok(version.AddItem(section.Id, "A1", 1, ItemType.SingleChoice, true, null, ScoringDirection.Forward, Text("سؤال", "Question"), Now));
        Ok(version.AddOption(item.Id, "o1", 1, 1, Text("خيار", "Option"), Now));
        Ok(version.AddOption(item.Id, "o2", 2, 2, Text("خيار", "Option"), Now));

        Assert.Equal(QuestionnaireErrors.ContentReferencesMissing, ApproveVersion(version, Now, null).Error);
    }

    [Fact]
    public void Approval_needs_at_least_one_section()
    {
        var version = Ok(InstrumentVersion.CreateDraft(NewInstrument(), 1, Now));
        Ok(version.SetContentReferences("ar", "en", Now));

        Assert.Equal(QuestionnaireErrors.VersionHasNoSections, ApproveVersion(version, Now, null).Error);
    }

    [Fact]
    public void Approval_needs_an_item_in_every_section()
    {
        var version = DraftVersion();
        Ok(version.AddSection("C", Text("قسم", "Section"), 3, Now));

        Assert.Equal(QuestionnaireErrors.SectionHasNoItems, ApproveVersion(version, Now, null).Error);
    }

    [Fact]
    public void Approval_needs_two_options_for_every_single_choice_item()
    {
        var version = DraftVersion();
        Ok(version.AddItem(version.Sections[0].Id, "A3", 3, ItemType.SingleChoice, false, null, ScoringDirection.NotScored, Text("سؤال", "Question"), Now));

        Assert.Equal(QuestionnaireErrors.ItemNeedsTwoOptions, ApproveVersion(version, Now, null).Error);
    }

    [Fact]
    public void Effective_window_must_end_after_it_starts() =>
        Assert.Equal(
            QuestionnaireErrors.EffectiveWindowInvalid,
            ApproveVersion(DraftVersion(), Now, Now).Error);

    [Fact]
    public void Effectiveness_follows_status_and_window()
    {
        var draft = DraftVersion();
        var approved = DraftVersion();
        Ok(ApproveVersion(approved, Now, Now.AddDays(10)));

        Assert.False(draft.IsEffectiveAt(Now));
        Assert.False(approved.IsEffectiveAt(Now.AddSeconds(-1)));
        Assert.True(approved.IsEffectiveAt(Now));
        Assert.False(approved.IsEffectiveAt(Now.AddDays(10)));
    }

    [Fact]
    public void Retiring_sets_the_end_of_the_window_and_raises_an_event()
    {
        var version = ApprovedVersion();
        version.ClearDomainEvents();
        var later = Now.AddHours(1);

        Assert.True(version.Retire(Now, later).IsSuccess);

        Assert.Equal(InstrumentVersionStatus.Retired, version.Status);
        Assert.Equal(Now, version.EffectiveTo);
        Assert.Equal(later, version.RetiredAt);
        Assert.IsType<InstrumentVersionRetired>(Assert.Single(version.GetDomainEvents()));
        Assert.False(version.IsEffectiveAt(Now.AddSeconds(-1)));
    }

    [Fact]
    public void Retiring_cannot_move_an_end_that_is_already_set()
    {
        var version = DraftVersion();
        Ok(ApproveVersion(version, Now, Now.AddDays(10)));
        var afterTheEnd = Now.AddDays(20);

        Assert.Equal(QuestionnaireErrors.EffectiveToAlreadySet, version.Retire(Now.AddDays(5), afterTheEnd).Error);
        Assert.True(version.Retire(Now.AddDays(10), afterTheEnd).IsSuccess);
    }

    [Fact]
    public void Only_an_approved_version_can_be_retired() =>
        Assert.Equal(QuestionnaireErrors.VersionNotApproved, DraftVersion().Retire(Now, Now).Error);

    [Fact]
    public void Retiring_with_a_future_end_is_rejected()
    {
        var version = ApprovedVersion();
        version.ClearDomainEvents();

        Assert.Equal(QuestionnaireErrors.RetireInFuture, version.Retire(Now.AddDays(1), Now).Error);

        Assert.Equal(InstrumentVersionStatus.Approved, version.Status);
        Assert.Null(version.EffectiveTo);
        Assert.Null(version.RetiredAt);
        Assert.Empty(version.GetDomainEvents());
    }

    [Fact]
    public void Scheduling_an_end_keeps_the_version_effective_until_then()
    {
        var version = ApprovedVersion();
        version.ClearDomainEvents();
        var end = Now.AddDays(5);

        Assert.True(version.ScheduleEnd(end, Now).IsSuccess);

        Assert.Equal(InstrumentVersionStatus.Approved, version.Status);
        Assert.Equal(end, version.EffectiveTo);
        Assert.Equal(Now, version.UpdatedAt);
        Assert.Null(version.RetiredAt);
        Assert.Empty(version.GetDomainEvents());
        Assert.True(version.IsEffectiveAt(Now));
        Assert.True(version.IsEffectiveAt(end.AddSeconds(-1)));
        Assert.False(version.IsEffectiveAt(end));
        Assert.True(version.Retire(end, end).IsSuccess);
    }

    [Fact]
    public void Scheduling_an_end_twice_is_rejected()
    {
        var version = ApprovedVersion();
        Ok(version.ScheduleEnd(Now.AddDays(5), Now));

        Assert.Equal(QuestionnaireErrors.EffectiveToAlreadySet, version.ScheduleEnd(Now.AddDays(6), Now).Error);
        Assert.Equal(QuestionnaireErrors.EffectiveToAlreadySet, version.ScheduleEnd(Now.AddDays(5), Now).Error);
        Assert.Equal(Now.AddDays(5), version.EffectiveTo);
    }

    [Fact]
    public void Only_an_approved_version_can_have_its_end_scheduled() =>
        Assert.Equal(QuestionnaireErrors.VersionNotApproved, DraftVersion().ScheduleEnd(Now.AddDays(5), Now).Error);

    [Fact]
    public void A_scheduled_end_must_come_after_the_start()
    {
        var version = DraftVersion();
        Ok(ApproveVersion(version, Now.AddDays(10), null));

        Assert.Equal(QuestionnaireErrors.EffectiveWindowInvalid, version.ScheduleEnd(Now.AddDays(10), Now).Error);
        Assert.Null(version.EffectiveTo);
    }

    [Fact]
    public void Scheduling_an_end_needs_utc_times()
    {
        var version = ApprovedVersion();
        var gulf = TimeSpan.FromHours(4);

        Assert.Equal(CommonErrors.TimeNotUtc, version.ScheduleEnd(Now.AddDays(5).ToOffset(gulf), Now).Error);
        Assert.Equal(CommonErrors.TimeNotUtc, version.ScheduleEnd(Now.AddDays(5), Now.ToOffset(gulf)).Error);
    }

    [Fact]
    public void A_non_substantive_option_keeps_its_raw_value_and_is_flagged()
    {
        var version = DraftVersion();
        var item = version.Sections[0].Items[0];

        var option = Ok(version.AddOption(
            item.Id, "99", 4, 99, Text("يفضل عدم الإجابة", "Prefer not to answer"), Now, isNonSubstantive: true));

        Assert.True(option.IsNonSubstantive);
        Assert.Equal(99, option.Value);
        Assert.Equal(new[] { 1, 2, 3 }, item.GetSubstantiveOptions().Select(substantive => substantive.Value));
    }

    [Fact]
    public void An_item_has_at_most_one_non_substantive_option()
    {
        var version = DraftVersion();
        var item = version.Sections[0].Items[0];
        Ok(version.AddOption(item.Id, "99", 4, 99, Text("خيار", "Option"), Now, isNonSubstantive: true));

        Assert.Equal(
            QuestionnaireErrors.NonSubstantiveOptionTaken,
            version.AddOption(item.Id, "98", 5, 98, Text("خيار", "Option"), Now, isNonSubstantive: true).Error);
    }

    [Fact]
    public void Approval_counts_only_substantive_options()
    {
        var version = DraftVersion();
        var item = Ok(version.AddItem(
            version.Sections[0].Id, "A3", 3, ItemType.SingleChoice, true, null, ScoringDirection.Forward, Text("سؤال", "Question"), Now));
        Ok(version.AddOption(item.Id, "1", 1, 1, Text("خيار", "Option"), Now));
        Ok(version.AddOption(item.Id, "99", 2, 99, Text("خيار", "Option"), Now, isNonSubstantive: true));

        Assert.Equal(QuestionnaireErrors.ItemNeedsTwoOptions, ApproveVersion(version, Now, null).Error);
    }

    [Fact]
    public void Approval_needs_arabic_for_every_text_of_the_version()
    {
        var version = DraftVersion();
        var item = Ok(version.AddItem(
            version.Sections[0].Id, "A3", 3, ItemType.SingleChoice, true, null, ScoringDirection.Forward, Text("سؤال", "Question"), Now));
        Ok(version.AddOption(item.Id, "1", 1, 1, DraftText("Option"), Now));
        Ok(version.AddOption(item.Id, "2", 2, 2, Text("خيار", "Option"), Now));

        Assert.Equal(QuestionnaireErrors.ArabicTextMissing, ApproveVersion(version, Now, null).Error);
        Assert.Equal(InstrumentVersionStatus.Draft, version.Status);
    }

    [Fact]
    public void Approval_needs_arabic_for_the_instrument_name_and_description()
    {
        var instrument = Ok(Instrument.Create(
            "draft_named", InstrumentKind.FamilyVoices, DraftText("Instrument"), Text("وصف", "Description"), Now));
        var version = DraftVersion(instrument);

        Assert.Equal(
            QuestionnaireErrors.ArabicTextMissing,
            version.Approve(instrument, TestApproval(), Now, null, Now).Error);
    }

    [Theory]
    [InlineData("instrument name")]
    [InlineData("instrument description")]
    [InlineData("section title")]
    [InlineData("item prompt")]
    [InlineData("option label")]
    public void Approval_is_refused_when_any_one_text_lacks_arabic(string missing)
    {
        LocalizedText TextUnless(string place, string arabic, string english) =>
            place == missing ? DraftText(english) : Text(arabic, english);
        var instrument = Ok(Instrument.Create(
            "one_text_missing",
            InstrumentKind.FamilyVoices,
            TextUnless("instrument name", "أداة", "Instrument"),
            TextUnless("instrument description", "وصف", "Description"),
            Now));
        var version = Ok(InstrumentVersion.CreateDraft(instrument, 1, Now));
        Ok(version.SetContentReferences("test-source (ar)", "test-source (en)", Now));
        var section = Ok(version.AddSection("A", TextUnless("section title", "قسم", "Section"), 1, Now));
        var item = Ok(version.AddItem(
            section.Id, "A1", 1, ItemType.SingleChoice, true, null, ScoringDirection.Forward,
            TextUnless("item prompt", "سؤال", "Question"), Now));
        Ok(version.AddOption(item.Id, "o1", 1, 1, TextUnless("option label", "خيار", "Option"), Now));
        Ok(version.AddOption(item.Id, "o2", 2, 2, Text("خيار", "Option"), Now));

        Assert.False(version.HasCompleteText(instrument));
        Assert.Equal(QuestionnaireErrors.ArabicTextMissing, version.Approve(instrument, TestApproval(), Now, null, Now).Error);
        Assert.Equal(InstrumentVersionStatus.Draft, version.Status);
    }

    [Fact]
    public void A_fully_bilingual_draft_has_complete_text()
    {
        var version = DraftVersion();

        Assert.True(version.HasCompleteText(InstrumentOf(version)));
    }

    [Fact]
    public void Complete_text_cannot_be_judged_against_another_instrument() =>
        Assert.Throws<ArgumentException>(() => DraftVersion().HasCompleteText(NewInstrument()));

    [Fact]
    public void Approval_rejects_another_instrument() =>
        Assert.Equal(
            QuestionnaireErrors.InstrumentMismatch,
            DraftVersion().Approve(NewInstrument(), TestApproval(), Now, null, Now).Error);

    [Fact]
    public void A_reverse_scored_item_needs_consecutive_option_values()
    {
        var version = DraftVersion();
        var item = Ok(version.AddItem(
            version.Sections[0].Id, "A3", 3, ItemType.SingleChoice, true, null, ScoringDirection.Reverse, Text("سؤال", "Question"), Now));
        foreach (var value in new[] { 1, 2, 4 })
        {
            Ok(version.AddOption(item.Id, $"{value}", value, value, Text("خيار", "Option"), Now));
        }

        Assert.Equal(QuestionnaireErrors.ReverseItemScaleInvalid, ApproveVersion(version, Now, null).Error);
    }

    [Fact]
    public void A_reverse_scored_item_ignores_its_non_substantive_option_when_checking_the_scale()
    {
        var version = DraftVersion();
        var item = Ok(version.AddItem(
            version.Sections[0].Id, "A3", 3, ItemType.SingleChoice, true, null, ScoringDirection.Reverse, Text("سؤال", "Question"), Now));
        for (var value = 1; value <= 5; value++)
        {
            Ok(version.AddOption(item.Id, $"{value}", value, value, Text("خيار", "Option"), Now));
        }

        Ok(version.AddOption(item.Id, "99", 6, 99, Text("خيار", "Option"), Now, isNonSubstantive: true));

        Assert.True(ApproveVersion(version, Now, null).IsSuccess);
    }

    [Fact]
    public void A_not_scored_item_cannot_have_a_scoring_category()
    {
        var version = DraftVersion();

        Assert.Equal(
            QuestionnaireErrors.NotScoredItemHasCategory,
            version.AddItem(
                version.Sections[0].Id, "A3", 3, ItemType.SingleChoice, true, "finance", ScoringDirection.NotScored,
                Text("سؤال", "Question"), Now).Error);
    }

    [Fact]
    public void A_version_whose_directions_are_unspecified_can_still_be_published_for_data_collection()
    {
        var version = Ok(InstrumentVersion.CreateDraft(NewInstrument(), 1, Now));
        Ok(version.SetContentReferences("test-source (ar)", "test-source (en)", Now));
        var section = Ok(version.AddSection("A", Text("قسم", "Section"), 1, Now));
        var item = Ok(version.AddItem(
            section.Id, "Q04", 1, ItemType.SingleChoice, true, null, ScoringDirection.Unspecified, Text("سؤال", "Question"), Now));
        Ok(version.AddOption(item.Id, "1", 1, 1, Text("خيار", "Option"), Now));
        Ok(version.AddOption(item.Id, "2", 2, 2, Text("خيار", "Option"), Now));

        Assert.True(ApproveVersion(version, Now, null).IsSuccess);
    }
}
