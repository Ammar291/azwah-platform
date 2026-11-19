namespace FamilyResilience.Application.Assistant;

/// <summary>The default: no model writes the reply, so the consultant says the fixed supportive words (D-141).</summary>
public sealed class NoConsultantCrisisReply : IConsultantCrisisReply
{
    public bool IsConfigured => false;

    public Task<string?> WriteAsync(string language, string said, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
}
