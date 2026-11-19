using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Sessions;

internal static class SessionLoads
{
    public static SessionResponse ToResponse(this AssessmentSession session, InstrumentVersion version)
    {
        var inScope = version.GetItems()
            .Where(item => session.InstrumentSectionId is null || item.InstrumentSectionId == session.InstrumentSectionId)
            .ToList();
        var sectionCode = session.InstrumentSectionId is { } sectionId ? version.FindSection(sectionId)?.Code : null;
        return new SessionResponse(
            session.Id.Value,
            session.AssessmentAssignmentId.Value,
            session.FamilyMemberId.Value,
            session.InstrumentVersionId.Value,
            version.VersionNumber,
            sectionCode,
            session.Status,
            session.StartedAt,
            session.SubmittedAt,
            session.ScoredAt,
            inScope.Count(item => session.FindResponse(item.Id) is not null),
            inScope.Count(item => item.IsRequired));
    }

    /// <summary>The session if it exists and the caller may act for its member; otherwise "not found", revealing nothing.</summary>
    public static async Task<Result<AssessmentSession>> AccessibleAsync(
        IAssessmentSessionRepository sessions, IParticipantAccess access, Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await sessions.GetAsync(AssessmentSessionId.From(sessionId), cancellationToken);
        return session is not null && await access.CanActForMemberAsync(session.FamilyMemberId, cancellationToken)
            ? Result.Success(session)
            : Result.Failure<AssessmentSession>(SessionErrors.SessionNotFound);
    }
}
