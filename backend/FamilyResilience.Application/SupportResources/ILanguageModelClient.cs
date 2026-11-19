namespace FamilyResilience.Application.SupportResources;

/// <summary>
/// The only door to a language model. No provider is approved or configured; an adapter would implement this and
/// nothing else, and may throw <see cref="LanguageModelUnavailableException"/> when the provider cannot answer.
/// </summary>
public interface ILanguageModelClient
{
    string ProviderCode { get; }

    Task<LanguageModelReply> CompleteJsonAsync(LanguageModelRequest request, CancellationToken cancellationToken);
}
