using System.Text.Encodings.Web;
using System.Text.Json;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Application.Assistant;

public sealed class FamilyContextBuilderTests
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string HeadLabel(FamilyContext context) =>
        Assert.Single(context.Members, member => member.IsHeadOfHousehold).Label;

    [Fact]
    public async Task The_head_sees_each_scored_sessions_total_and_band()
    {
        var family = SyntheticFamily.HeadSignsIn();

        var context = await family.Builder().BuildAsync(AssistantLanguages.English, TestContext.Current.CancellationToken);

        Assert.NotNull(context);
        Assert.Equal(ScoreVisibility.Visible, context.Results);
        var head = HeadLabel(context);
        Assert.Equal((head, (HouseholdRelationship?)null, true, true),
            (context.Caller.Member, context.Caller.Relationship, context.Caller.IsHeadOfHousehold, context.Caller.ManagesHousehold));
        var session = Assert.Single(context.Sessions);
        Assert.Equal((head, SessionStatus.Scored), (session.Member, session.Status));
        Assert.Equal(new FamilyContextResult(ScoreStatus.Complete, 3m, SyntheticFamily.BandCode), session.Result);
        Assert.Equal(1, family.ScoreResults.Reads);
    }

    [Fact]
    public async Task A_manager_answering_for_the_head_gets_the_answers_but_no_score_and_no_score_is_read()
    {
        var family = SyntheticFamily.SpouseManagesForTheHead();

        var context = await family.Builder().BuildAsync(AssistantLanguages.English, TestContext.Current.CancellationToken);

        Assert.NotNull(context);
        Assert.Equal(ScoreVisibility.HeadOfHouseholdOnly, context.Results);
        var spouse = Assert.Single(context.Members, member => member.Relationship == HouseholdRelationship.Spouse);
        Assert.True(spouse.IsCaller);
        Assert.Equal((spouse.Label, (HouseholdRelationship?)HouseholdRelationship.Spouse, false, true),
            (context.Caller.Member, context.Caller.Relationship, context.Caller.IsHeadOfHousehold, context.Caller.ManagesHousehold));
        var session = Assert.Single(context.Sessions);
        Assert.Equal(HeadLabel(context), session.Member);
        Assert.Null(session.Result);
        Assert.Equal(2, session.Answers.Count);
        Assert.Equal(0, family.ScoreResults.Reads);
        Assert.DoesNotContain(SyntheticFamily.BandCode, JsonSerializer.Serialize(context, Json), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AssistantLanguages.English, "Synthetic question one", "Synthetic high 1", "Synthetic question two", "Synthetic low 2", "Synthetic instrument")]
    [InlineData(AssistantLanguages.Arabic, "السؤال التجريبي الأول", "خيار مرتفع 1", "السؤال التجريبي الثاني", "خيار منخفض 2", "أداة تجريبية")]
    public async Task Answers_read_as_the_questions_prompt_and_the_chosen_options_label_in_the_language_in_question_order(
        string language, string firstQuestion, string firstAnswer, string secondQuestion, string secondAnswer, string instrument)
    {
        var family = SyntheticFamily.HeadSignsIn();

        var context = await family.Builder().BuildAsync(language, TestContext.Current.CancellationToken);

        Assert.NotNull(context);
        Assert.Equal(language, context.Language);
        var session = Assert.Single(context.Sessions);
        Assert.Equal(
            [new FamilyContextAnswer(firstQuestion, firstAnswer), new FamilyContextAnswer(secondQuestion, secondAnswer)],
            session.Answers);
        Assert.Equal(instrument, session.InstrumentName);
        Assert.Equal(instrument, Assert.Single(context.Questionnaires).InstrumentName);
    }

    [Fact]
    public async Task The_context_describes_the_household_its_members_consents_questionnaires_and_next_steps()
    {
        var family = SyntheticFamily.HeadSignsIn();

        var context = await family.Builder().BuildAsync(AssistantLanguages.English, TestContext.Current.CancellationToken);

        Assert.NotNull(context);
        Assert.Equal(Today, context.Today);
        Assert.Equal(new FamilyContextHousehold(family.Household.Reference.Value, HouseholdStatus.Active, false), context.Household);
        Assert.Equal(["member_1", "member_2", "member_3"], context.Members.Select(member => member.Label));
        Assert.Equal(
            [
                (null, 42, EmploymentStatus.Employed, true, true, true, true),
                (HouseholdRelationship.Spouse, 39, EmploymentStatus.Employed, false, false, false, false),
                (HouseholdRelationship.Child, 9, null, false, false, false, false),
            ],
            context.Members
                .OrderByDescending(member => member.Age)
                .Select(member => (member.Relationship, member.Age, member.EmploymentStatus, member.IsHeadOfHousehold,
                    member.IsPrimaryIncomeEarner, member.HasAccount, member.IsCaller)));
        var head = HeadLabel(context);
        Assert.Equal(new FamilyContextConsent(head, ConsentType.Participation, true, Now, null), Assert.Single(context.Consents));
        var questionnaire = Assert.Single(context.Questionnaires);
        Assert.Equal((head, "synthetic_wave", Now.AddDays(30)), (questionnaire.Member, questionnaire.Wave, questionnaire.DueAt));
        Assert.Equal(new FamilyContextNextStep(head, SyntheticFamily.CategoryCode, null, Now), Assert.Single(context.NextSteps));
    }

    [Fact]
    public async Task The_context_holds_no_database_id_subject_or_platform_user()
    {
        var family = SyntheticFamily.HeadSignsIn();

        var context = await family.Builder().BuildAsync(AssistantLanguages.English, TestContext.Current.CancellationToken);

        var json = JsonSerializer.Serialize(context, Json);
        var identifiers = family.Household.Members.Select(member => member.Id.Value)
            .Append(family.Household.Id.Value)
            .Append(family.CallerId.Value)
            .Append(family.Session.Id.Value)
            .Append(family.Assignment.Id.Value)
            .Append(family.Version.Id.Value)
            .Append(family.Score.Id.Value)
            .Append(family.Recommendation.Id.Value)
            .Append(family.Consent.Id.Value)
            .Append(family.Policy.Id.Value)
            .Select(id => id.ToString());
        Assert.All(identifiers, id => Assert.DoesNotContain(id, json, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(SyntheticFamily.Subject, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_member_with_their_own_account_is_left_out_of_the_managers_context()
    {
        var family = SyntheticFamily.HeadSignsIn();
        var spouse = family.Household.Members[1];
        Ok(family.Household.LinkAccount(PlatformUserId.New(), spouse.Id, Now));

        var context = await family.Builder().BuildAsync(AssistantLanguages.English, TestContext.Current.CancellationToken);

        Assert.NotNull(context);
        Assert.Equal(["member_1", "member_2"], context.Members.Select(member => member.Label));
        Assert.Equal(
            [(HouseholdRelationship?)null, HouseholdRelationship.Child],
            context.Members.OrderByDescending(member => member.Age).Select(member => member.Relationship));
    }

    [Fact]
    public async Task A_caller_without_a_household_gets_no_context()
    {
        var family = SyntheticFamily.HeadSignsIn();
        var cancellationToken = TestContext.Current.CancellationToken;

        var unregistered = await family.BuilderFor(null).BuildAsync(AssistantLanguages.English, cancellationToken);
        var withoutHousehold = await family.BuilderFor(new CallerRecord(PlatformUserId.New(), null))
            .BuildAsync(AssistantLanguages.English, cancellationToken);

        Assert.Null(unregistered);
        Assert.Null(withoutHousehold);
        Assert.Equal(0, family.ScoreResults.Reads);
    }

    [Fact]
    public async Task Only_english_and_arabic_are_accepted()
    {
        var family = SyntheticFamily.HeadSignsIn();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            family.Builder().BuildAsync("fr", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_context_never_prints_its_content()
    {
        var family = SyntheticFamily.HeadSignsIn();

        var context = await family.Builder().BuildAsync(AssistantLanguages.English, TestContext.Current.CancellationToken);

        Assert.NotNull(context);
        var printed = context + string.Join(' ', context.Sessions) + string.Join(' ', context.Sessions[0].Answers);
        Assert.DoesNotContain("Synthetic question", printed, StringComparison.Ordinal);
        Assert.DoesNotContain(SyntheticFamily.BandCode, printed, StringComparison.Ordinal);
    }
}
