using System.Net;
using System.Text.Json;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.IntegrationTests.Support;

namespace FamilyResilience.IntegrationTests.Assistant;

/// <summary>
/// The family context a model receives, built from real records after a scored check-in on <see cref="ParticipantApi"/>
/// (section A's lowest answers score 2, <c>band_low</c>, which routes to <c>synthetic_support</c>). Only the household's
/// own head gets score values (D-103). Every family and every question is synthetic.
/// </summary>
[Collection(ParticipantApiCollection.Name)]
public sealed class AssistantContextTests(ParticipantApi api)
{
    private ApiFactory Factory => api.Factory;

    private static object Chat(string language) => new { language, messages = new[] { new { role = "user", text = "How did we do?" } } };

    private static async Task<FamilyContext> ContextSentAsync(
        RecordingAssistantModel model, Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host, string subject, string language)
    {
        using var client = host.Client(subject);
        using var response = await client.PostJsonAsync("/api/v1/assistant/replies", Chat(language), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // What the model actually read: the context JSON in the step, read back into the record.
        var context = RecordingAssistantModel.FamilyContextOf(model.Steps[^1]);
        Assert.NotNull(context);
        return context;
    }

    [Fact]
    public async Task The_head_gets_the_score_and_band_with_the_answers_and_next_steps()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(), Rosters.Parent(68));
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, enrolment.Self, "A", 0, cancellationToken);
        using var submitted = await Journeys.SubmitAsync(enrolment.Client, sessionId, Guid.NewGuid(), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var model = RecordingAssistantModel.Replying("A synthetic reply.");
        await using var host = Factory.With(assistant: model);

        var context = await ContextSentAsync(model, host, enrolment.Subject, AssistantLanguages.English);

        Assert.Equal(ScoreVisibility.Visible, context.Results);
        Assert.True(context.Caller.IsHeadOfHousehold);
        var head = Assert.Single(context.Members, member => member.IsHeadOfHousehold).Label;
        var session = Assert.Single(context.Sessions);
        Assert.Equal((head, SessionStatus.Scored), (session.Member, session.Status));
        Assert.Equal(new FamilyContextResult(ScoreStatus.Complete, 2m, "band_low"), session.Result);
        Assert.Equal(2, session.Answers.Count);
        Assert.All(session.Answers, answer => Assert.Equal(new FamilyContextAnswer("Question", "Option"), answer));
        Assert.Equal((head, "synthetic_support"), (Assert.Single(context.NextSteps).Member, context.NextSteps[0].CategoryCode));
        Assert.Contains(context.Consents, consent => consent.Member == head && consent.Purpose == ConsentType.Participation && consent.IsActive);
        Assert.Contains(context.Questionnaires, questionnaire => questionnaire.Member == head && questionnaire.Status == AssignmentStatus.Completed);
    }

    [Fact]
    public async Task A_manager_answering_for_the_head_gets_the_answers_but_never_the_score()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(self: false), Rosters.Spouse(self: true));
        var head = enrolment.Members.Single(member => !member.IsCaller).Id;
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, head, "A", 0, cancellationToken);
        using var submitted = await Journeys.SubmitAsync(enrolment.Client, sessionId, Guid.NewGuid(), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var model = RecordingAssistantModel.Replying("A synthetic reply.");
        await using var host = Factory.With(assistant: model);

        var context = await ContextSentAsync(model, host, enrolment.Subject, AssistantLanguages.English);

        Assert.Equal(ScoreVisibility.HeadOfHouseholdOnly, context.Results);
        Assert.False(context.Caller.IsHeadOfHousehold);
        Assert.True(context.Caller.ManagesHousehold);
        var session = Assert.Single(context.Sessions);
        Assert.Equal(SessionStatus.Scored, session.Status);
        Assert.Null(session.Result);
        Assert.Equal(2, session.Answers.Count);
        var json = JsonSerializer.Serialize(context);
        Assert.DoesNotContain("band_low", json, StringComparison.Ordinal);
        Assert.DoesNotContain(sessionId.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(head.ToString(), json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task In_arabic_the_answers_read_in_arabic()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(), Rosters.Parent(68));
        await Journeys.AnsweredSessionAsync(enrolment, api, enrolment.Self, "A", 1, cancellationToken);
        var model = RecordingAssistantModel.Replying("رد تجريبي.");
        await using var host = Factory.With(assistant: model);

        var context = await ContextSentAsync(model, host, enrolment.Subject, AssistantLanguages.Arabic);

        Assert.Equal(AssistantLanguages.Arabic, context.Language);
        var session = Assert.Single(context.Sessions);
        Assert.Equal(SessionStatus.InProgress, session.Status);
        Assert.Null(session.Result);
        Assert.All(session.Answers, answer => Assert.Equal(new FamilyContextAnswer("سؤال", "خيار"), answer));
        Assert.Equal("قسم", session.SectionTitle);
    }
}
