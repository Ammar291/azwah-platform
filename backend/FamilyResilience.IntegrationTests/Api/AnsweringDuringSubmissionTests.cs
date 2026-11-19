using System.Net;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.Infrastructure.Persistence;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Api;

/// <summary>
/// Saving answers takes the session lock that submission holds, so answers that arrive while the session is being
/// submitted wait for the submission and are then refused as an expected conflict, never a server error.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AnsweringDuringSubmissionTests(ApiFactory factory)
{
    private static object HighestAnswers(InstrumentVersion version) => new
    {
        responses = version.GetItems()
            .Select(item => new { itemId = item.Id.Value, optionId = item.Options.OrderByDescending(option => option.Value).First().Id.Value })
            .ToArray(),
    };

    [Fact]
    public async Task Answers_saved_while_the_session_is_being_submitted_return_409_not_in_progress()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var participant = await SeededParticipant.CreateAsync(factory, cancellationToken);
        using var started = await participant.Client.PostJsonAsync($"/api/v1/assignments/{participant.AssignmentId}/sessions", new { }, cancellationToken);
        var sessionId = AssessmentSessionId.From((await started.ReadAsync<SessionResponse>(cancellationToken)).Id);
        using var answered = await participant.Client.PutJsonAsync($"/api/v1/sessions/{sessionId.Value}/responses", participant.Answers(), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        Task<HttpResponseMessage>? saving = null;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var unitOfWork = services.GetRequiredService<IUnitOfWork>();
            var submitted = await unitOfWork.ExecuteInTransactionAsync(
                async token =>
                {
                    await services.GetRequiredService<ISessionLock>().LockAsync(sessionId, token);
                    saving = participant.Client.PutJsonAsync($"/api/v1/sessions/{sessionId.Value}/responses", HighestAnswers(participant.Version), token);
                    await PostgresWait.UntilAnotherConnectionWaitsOnAsync(services.GetRequiredService<FamilyResilienceDbContext>(), token);

                    var session = await services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(sessionId, token);
                    var assignment = await services.GetRequiredService<IAssessmentAssignmentRepository>()
                        .GetAsync(AssessmentAssignmentId.From(participant.AssignmentId), token);
                    TestData.Ok(session!.Submit(participant.Version, assignment!, TestData.Now));
                    var committed = await unitOfWork.CommitAsync(token);
                    return committed.IsFailure ? Result.Failure<bool>(committed.Error!) : Result.Success(true);
                },
                cancellationToken);
            TestData.Ok(submitted);
        }

        using var response = await saving!;

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("sessions.not_in_progress", await response.ProblemCodeAsync(cancellationToken));
        var stored = await factory.QueryAsync(db => db.AssessmentSessions.Include(session => session.Responses)
            .SingleAsync(candidate => candidate.Id == sessionId, cancellationToken));
        Assert.Equal(SessionStatus.Submitted, stored.Status);
        Assert.Equal(
            participant.Version.GetItems().Select(item => item.Options.MinBy(option => option.Value)!.Id.Value).Order(),
            stored.Responses.Select(answer => answer.ResponseOptionId.Value).Order());
    }
}
