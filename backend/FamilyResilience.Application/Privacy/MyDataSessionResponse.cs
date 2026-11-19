using FamilyResilience.Application.Sessions;

namespace FamilyResilience.Application.Privacy;

/// <summary>One session in the data export, with every answer in it.</summary>
public sealed record MyDataSessionResponse(SessionResponse Session, IReadOnlyList<AnswerResponse> Answers);
