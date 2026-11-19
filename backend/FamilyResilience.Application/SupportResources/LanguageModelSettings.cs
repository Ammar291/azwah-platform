using System.Globalization;

namespace FamilyResilience.Application.SupportResources;

/// <summary>
/// The optional language-model path, from configuration section <c>SupportResources:LanguageModel</c>. Off unless
/// <see cref="Enabled"/> parses as <c>true</c>; switched off, every other value is ignored. Switched on, the API refuses
/// to start while <see cref="Problems"/> names anything: no approval reference naming the approving decision, no
/// registered <see cref="ILanguageModelClient"/> (none ships), or a value out of range. No provider is approved.
/// </summary>
public sealed record LanguageModelSettings(
    bool Enabled, string? ApprovalReference, int TimeoutSeconds, int MaxAttempts, int MaxCandidates, int MaxOutputTokens)
{
    public const string Section = "SupportResources:LanguageModel";

    public const int MaxApprovalReferenceLength = 100;

    public static LanguageModelSettings Default { get; } = new(false, null, 5, 2, 12, 256);

    /// <summary>
    /// Reads the section through <paramref name="read"/>. An absent number keeps its default; a present number that does
    /// not parse becomes 0, which is out of range, so a typo cannot quietly fall back to a default.
    /// </summary>
    public static LanguageModelSettings From(Func<string, string?> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        var approvalReference = read($"{Section}:ApprovalReference");
        return new LanguageModelSettings(
            bool.TryParse(read($"{Section}:Enabled"), out var enabled) && enabled,
            string.IsNullOrWhiteSpace(approvalReference) ? null : approvalReference.Trim(),
            Number(read($"{Section}:TimeoutSeconds"), Default.TimeoutSeconds),
            Number(read($"{Section}:MaxAttempts"), Default.MaxAttempts),
            Number(read($"{Section}:MaxCandidates"), Default.MaxCandidates),
            Number(read($"{Section}:MaxOutputTokens"), Default.MaxOutputTokens));
    }

    /// <summary>Why a switched-on model cannot run, as fixed codes; empty when switched off.</summary>
    public IReadOnlyList<string> Problems(bool clientRegistered)
    {
        if (!Enabled)
        {
            return [];
        }

        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(ApprovalReference) || ApprovalReference.Length > MaxApprovalReferenceLength)
        {
            problems.Add("approval_reference_missing");
        }

        if (!clientRegistered)
        {
            problems.Add("provider_missing");
        }

        if (TimeoutSeconds is < 1 or > 30)
        {
            problems.Add("timeout_out_of_range");
        }

        if (MaxAttempts is < 1 or > 3)
        {
            problems.Add("attempts_out_of_range");
        }

        if (MaxCandidates is < 1 or > 20)
        {
            problems.Add("candidates_out_of_range");
        }

        if (MaxOutputTokens is < 64 or > 1024)
        {
            problems.Add("output_tokens_out_of_range");
        }

        return problems;
    }

    private static int Number(string? configured, int fallback) =>
        configured is null
            ? fallback
            : int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
}
