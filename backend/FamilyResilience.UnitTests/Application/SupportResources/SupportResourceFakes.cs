using FamilyResilience.Application.SupportResources;
using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.UnitTests.Application.SupportResources;

/// <summary>
/// A search that records every call and answers with the resources it was given. When <see cref="StrictAnswer"/> is set,
/// strict (half-coverage) searches answer with it instead, so a test can tell the strict answer from the candidates.
/// </summary>
internal sealed class FakeSupportResourceSearch(params SupportResource[] answer) : ISupportResourceSearch
{
    public List<SupportResourceSearchCriteria> Searches { get; } = [];

    public SupportResource[]? StrictAnswer { get; init; }

    public Task<IReadOnlyList<SupportResource>> SearchAsync(
        SupportResourceSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        Searches.Add(criteria);
        return Task.FromResult<IReadOnlyList<SupportResource>>(
            criteria.Coverage == TermCoverage.Half && StrictAnswer is { } strict ? strict : answer);
    }

    public Task<SupportResource?> GetDiscoverableAsync(
        SupportResourceId id,
        IReadOnlyCollection<SupportResourceVisibility> visibilities,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(answer.FirstOrDefault(resource => resource.Id == id && visibilities.Contains(resource.Visibility)));

    public Task<IReadOnlyList<SupportResource>> ListAsync(
        IReadOnlyCollection<SupportResourceVisibility> visibilities,
        DateTimeOffset now,
        int skip,
        int take,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SupportResource>>(
            answer.Where(resource => visibilities.Contains(resource.Visibility)).Skip(skip).Take(take).ToList());

    public Task<int> CountAsync(
        IReadOnlyCollection<SupportResourceVisibility> visibilities, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        Task.FromResult(answer.Count(resource => visibilities.Contains(resource.Visibility)));
}

/// <summary>
/// A language model that plays a fixed script, one step per call (the last step repeats), and records every request.
/// No provider is involved: each step returns a reply, throws, or waits.
/// </summary>
internal sealed class ScriptedLanguageModelClient(params Func<CancellationToken, Task<LanguageModelReply>>[] steps)
    : ILanguageModelClient
{
    public string ProviderCode => "scripted";

    public List<LanguageModelRequest> Requests { get; } = [];

    public Task<LanguageModelReply> CompleteJsonAsync(LanguageModelRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return steps[Math.Min(Requests.Count, steps.Length) - 1](cancellationToken);
    }

    public static Func<CancellationToken, Task<LanguageModelReply>> Reply(string json, int inputTokens = 0, int outputTokens = 0) =>
        _ => Task.FromResult(new LanguageModelReply(json, inputTokens, outputTokens));

    public static Func<CancellationToken, Task<LanguageModelReply>> Throw(Exception exception) =>
        _ => Task.FromException<LanguageModelReply>(exception);

    /// <summary>Never answers, but stops when the token it was given is cancelled.</summary>
    public static Func<CancellationToken, Task<LanguageModelReply>> Hang() =>
        async cancellationToken =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("A delay without end finished.");
        };

    /// <summary>Never answers and ignores the token, as a badly written adapter might.</summary>
    public static Func<CancellationToken, Task<LanguageModelReply>> HangIgnoringCancellation() =>
        _ => new TaskCompletionSource<LanguageModelReply>().Task;
}
