using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Sessions;

internal static class AnswerMappings
{
    public static SessionAnswersResponse ToAnswers(this AssessmentSession session, InstrumentVersion version)
    {
        var order = version.Sections
            .OrderBy(section => section.DisplayOrder)
            .SelectMany(section => section.Items.OrderBy(item => item.DisplayOrder))
            .Select((item, index) => (item.Id, index))
            .ToDictionary(pair => pair.Id, pair => pair.index);
        return new SessionAnswersResponse(
            session.Id.Value,
            session.Status,
            session.Responses
                .OrderBy(response => order.GetValueOrDefault(response.InstrumentItemId, int.MaxValue))
                .Select(response => new AnswerResponse(
                    response.InstrumentItemId.Value, response.ResponseOptionId.Value, response.AnsweredAt, response.LastModifiedAt))
                .ToList());
    }
}
