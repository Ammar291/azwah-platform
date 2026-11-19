namespace FamilyResilience.Application.Assistant;

/// <summary>
/// One place to improve: <see cref="Area"/> is where, <see cref="Reason"/> why it matters, <see cref="Steps"/> how to
/// start. <see cref="SectionCode"/> says which area it is about when a model writes a whole report at once.
/// <see cref="SupportCodes"/> are the services a model picked from the options the graph offered it, which the server checks
/// (D-115). <see cref="ToString"/> prints nothing about it.
/// </summary>
public sealed record ScoreReportImprovement(string Area, string Reason, IReadOnlyList<string> Steps)
{
    public string? SectionCode { get; init; }

    public IReadOnlyList<string>? SupportCodes { get; init; }

    public override string ToString() => nameof(ScoreReportImprovement);
}
