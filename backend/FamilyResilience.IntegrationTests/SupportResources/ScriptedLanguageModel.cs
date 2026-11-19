using System.Collections.Concurrent;
using System.Text.Json;
using FamilyResilience.Application.SupportResources;

namespace FamilyResilience.IntegrationTests.SupportResources;

/// <summary>
/// A stand-in language model for test hosts only: it records every request and answers with <see cref="Respond"/>,
/// which a test may change between searches. No provider is approved or configured; nothing here calls one.
/// </summary>
internal sealed class ScriptedLanguageModel(Func<LanguageModelRequest, LanguageModelReply> respond) : ILanguageModelClient
{
    private readonly ConcurrentQueue<LanguageModelRequest> _requests = new();

    public string ProviderCode => "scripted";

    public Func<LanguageModelRequest, LanguageModelReply> Respond { get; set; } = respond;

    public IReadOnlyList<LanguageModelRequest> Requests => [.. _requests];

    public Task<LanguageModelReply> CompleteJsonAsync(LanguageModelRequest request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(request);
        return Task.FromResult(Respond(request));
    }

    /// <summary>Selects every candidate, last first.</summary>
    public static ScriptedLanguageModel Reversing() => new(ReverseCandidates);

    /// <summary>Fails as a provider that will not recover.</summary>
    public static ScriptedLanguageModel Failing() => new(Fail);

    /// <summary>Always answers with the same JSON.</summary>
    public static ScriptedLanguageModel Answering(string json) => new(_ => new LanguageModelReply(json, 10, 2));

    public static LanguageModelReply ReverseCandidates(LanguageModelRequest request)
    {
        var handles = Candidates(request).Select(candidate => candidate.Handle).Reverse().ToList();
        return new LanguageModelReply(JsonSerializer.Serialize(new { selected = handles }), 50, 5);
    }

    public static LanguageModelReply Fail(LanguageModelRequest request) =>
        throw new LanguageModelUnavailableException(transient: false);

    /// <summary>The candidates a request offered, in the order offered.</summary>
    public static IReadOnlyList<(string Handle, string NameEn)> Candidates(LanguageModelRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var data = JsonDocument.Parse(request.Data);
        return data.RootElement.GetProperty("candidates").EnumerateArray()
            .Select(candidate => (
                candidate.GetProperty("handle").GetString() ?? string.Empty,
                candidate.GetProperty("name_en").GetString() ?? string.Empty))
            .ToList();
    }
}
