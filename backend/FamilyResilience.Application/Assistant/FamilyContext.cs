using FamilyResilience.Application.Sessions;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// What Azwah holds about the caller's family, as the caller may see it, for a model to answer from. Built on the server
/// for the signed-in caller only (<see cref="FamilyContextBuilder"/>): the members they may act for (D-49), and score
/// values only when the caller is the household's head (D-103). It holds no names, identity-provider details, tokens or
/// database IDs; members are named by <see cref="FamilyContextMember.Label"/>. Sensitive personal information: never
/// log it, store it or put it in a metric. <see cref="ToString"/> prints counts only.
/// </summary>
/// <param name="Language">The language every text in the context is in: <c>en</c> or <c>ar</c>.</param>
/// <param name="Today">The server's date (UTC), against which ages and due dates read.</param>
/// <param name="Results">
/// <see cref="ScoreVisibility.Visible"/> when the caller is the head and sessions carry their results;
/// <see cref="ScoreVisibility.HeadOfHouseholdOnly"/> when results belong to the head and none are included.
/// </param>
public sealed record FamilyContext(
    string Language,
    DateOnly Today,
    FamilyContextCaller Caller,
    FamilyContextHousehold Household,
    IReadOnlyList<FamilyContextMember> Members,
    IReadOnlyList<FamilyContextConsent> Consents,
    IReadOnlyList<FamilyContextQuestionnaire> Questionnaires,
    IReadOnlyList<FamilyContextSession> Sessions,
    ScoreVisibility Results,
    IReadOnlyList<FamilyContextNextStep> NextSteps)
{
    public override string ToString() =>
        $"FamilyContext {{ Language = {Language}, Members = {Members.Count}, Consents = {Consents.Count}, Questionnaires = {Questionnaires.Count}, Sessions = {Sessions.Count}, Results = {Results}, NextSteps = {NextSteps.Count} }}";
}
