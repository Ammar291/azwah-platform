namespace FamilyResilience.Application.SupportResources;

/// <summary>
/// The optional model path (spec §7.4), used only when <see cref="LanguageModelSettings.Enabled"/> is true, which the API
/// refuses unless an approved <see cref="ILanguageModelClient"/> is registered; none ships. The model may only choose and
/// order candidates the deterministic search found, named by handles; what a family sees is re-rendered from directory
/// rows. Every model failure answers with the deterministic search and a <see cref="DiscoveryFallback"/> code, never an
/// exception; only the caller's cancellation propagates. Writes no logs: outcomes are counted by fixed codes.
/// </summary>
public sealed class LanguageModelSupportResourceAssistant(
    DeterministicSupportResourceAssistant deterministic,
    ISupportResourceSearch search,
    ILanguageModelClient client,
    LanguageModelSettings settings,
    SupportResourceMetrics metrics,
    TimeProvider timeProvider) : ISupportResourceAssistant
{
    public async Task<SupportResourceDiscovery> DiscoverAsync(SupportResourceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Without words there is nothing for a model to judge (a category browse); answer as the search does.
        if (query.Terms.Count == 0 || string.IsNullOrWhiteSpace(query.Text))
        {
            return await deterministic.DiscoverAsync(query, cancellationToken);
        }

        var candidates = await search.SearchAsync(
            new SupportResourceSearchCriteria(
                query.Terms,
                query.Categories,
                query.Audiences,
                query.Visibilities,
                timeProvider.GetUtcNow(),
                TermCoverage.AnyTerm,
                settings.MaxCandidates),
            cancellationToken);
        if (candidates.Count == 0)
        {
            return new SupportResourceDiscovery(DiscoveryMode.Deterministic, null, []);
        }

        var prompt = SupportResourceSelectionPrompt.Build(
            SupportResourceQueryRedactor.Redact(query.Text), candidates, query.Limit, settings.MaxOutputTokens);
        var (verdict, failure) = await AskAsync(prompt, query.Limit, cancellationToken);
        if (verdict is null)
        {
            return await FallbackAsync(query, failure, cancellationToken);
        }

        return verdict.Kind switch
        {
            SelectionVerdictKind.Accepted when verdict.Selected.Count > 0 =>
                new SupportResourceDiscovery(DiscoveryMode.LanguageModel, null, verdict.Selected.Take(query.Limit).ToList()),
            // The model may narrow and reorder, never hide everything.
            SelectionVerdictKind.Accepted => await FallbackAsync(query, DiscoveryFallback.ModelSelectedNone, cancellationToken),
            SelectionVerdictKind.Ungrounded => await FallbackAsync(query, DiscoveryFallback.ModelOutputUngrounded, cancellationToken),
            _ => await FallbackAsync(query, DiscoveryFallback.ModelOutputInvalid, cancellationToken),
        };
    }

    /// <summary>
    /// Calls the model up to <see cref="LanguageModelSettings.MaxAttempts"/> times, each under its own timeout, retrying
    /// only timeouts and transient faults. Returns the guard's verdict on the first reply, or why there was none.
    /// </summary>
    private async Task<(SelectionVerdict? Verdict, DiscoveryFallback Failure)> AskAsync(
        SelectionPrompt prompt, int limit, CancellationToken cancellationToken)
    {
        var failure = DiscoveryFallback.ModelUnavailable;
        for (var attempt = 1; attempt <= settings.MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
            LanguageModelReply? reply;
            try
            {
                // WaitAsync bounds the attempt even when a client ignores its token.
                reply = await client.CompleteJsonAsync(prompt.Request, timeout.Token).WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                metrics.ModelCallCompleted(ModelCallResult.Timeout);
                failure = DiscoveryFallback.ModelTimeout;
                continue;
            }
            catch (LanguageModelUnavailableException fault) when (fault.Transient)
            {
                metrics.ModelCallCompleted(ModelCallResult.Unavailable);
                failure = DiscoveryFallback.ModelUnavailable;
                continue;
            }
            catch (Exception)
            {
                // Broad on purpose: a permanent provider fault or any adapter bug must not cost the person their search
                // (spec §7.4 step 8). The deterministic answer is given instead; the exception is dropped unlogged,
                // because an adapter's message could quote the request.
                metrics.ModelCallCompleted(ModelCallResult.Unavailable);
                return (null, DiscoveryFallback.ModelUnavailable);
            }

            if (reply is null)
            {
                metrics.ModelCallCompleted(ModelCallResult.Invalid);
                return (null, DiscoveryFallback.ModelOutputInvalid);
            }

            metrics.ModelTokensUsed(reply.InputTokens, reply.OutputTokens);
            var verdict = SupportResourceSelectionGuard.Check(reply.Json, prompt.Handles, limit);
            metrics.ModelCallCompleted(verdict.Kind switch
            {
                SelectionVerdictKind.Accepted => ModelCallResult.Ok,
                SelectionVerdictKind.Ungrounded => ModelCallResult.Ungrounded,
                _ => ModelCallResult.Invalid,
            });
            return (verdict, failure);
        }

        return (null, failure);
    }

    /// <summary>The strict deterministic answer, marked with why the model's was not used.</summary>
    private async Task<SupportResourceDiscovery> FallbackAsync(
        SupportResourceQuery query, DiscoveryFallback fallback, CancellationToken cancellationToken)
    {
        // A caller who has gone gets a cancellation, not a fallback, whatever the client threw meanwhile.
        cancellationToken.ThrowIfCancellationRequested();
        var answer = await deterministic.DiscoverAsync(query, cancellationToken);
        return answer with { Mode = DiscoveryMode.Deterministic, Fallback = fallback };
    }
}
