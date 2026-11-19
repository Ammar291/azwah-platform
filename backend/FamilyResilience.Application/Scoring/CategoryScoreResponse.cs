namespace FamilyResilience.Application.Scoring;

public sealed record CategoryScoreResponse(string CategoryCode, decimal Score, int ItemCount);
