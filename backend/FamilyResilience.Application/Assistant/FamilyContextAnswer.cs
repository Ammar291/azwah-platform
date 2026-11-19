namespace FamilyResilience.Application.Assistant;

/// <summary>A question's prompt and the label of the option chosen, both from the exact version answered.</summary>
public sealed record FamilyContextAnswer(string Question, string Answer)
{
    /// <summary>Never prints the question or the answer, so neither can reach a log through string formatting.</summary>
    public override string ToString() => $"FamilyContextAnswer {{ QuestionLength = {Question.Length}, AnswerLength = {Answer.Length} }}";
}
