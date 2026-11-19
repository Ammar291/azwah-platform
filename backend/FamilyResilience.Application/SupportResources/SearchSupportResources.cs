using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Safeguarding;
using Microsoft.Extensions.Logging;

namespace FamilyResilience.Application.SupportResources;

/// <summary>
/// Searches the approved directory for the caller. Visibility comes from the token's roles. The text is parsed into
/// terms and handed to the assistant; it is never stored, logged or audited, and only fixed codes reach the metrics.
/// A participant's text first goes through the crisis phrase screen (D-135, D-138): a match starts recording for staff
/// as fixed codes (source <c>support_search</c>) beside the search, so a slow database never holds the search back, the
/// answer waits for the recording, and it carries <c>crisis_support</c>. A server fault after a match is caught and
/// answered <see cref="SupportResourceSearchErrors.Faulted"/> with the notice, logging only the exception's type, because
/// the notice must never depend on the database; without a match a fault propagates as before. Staff searches are never
/// screened, and the search itself is the same either way.
/// </summary>
public sealed partial class SearchSupportResources(
    ISupportResourceAssistant assistant,
    ICurrentUser currentUser,
    CrisisSignalScreen screen,
    ISafetySignalRecorder recorder,
    SupportResourceMetrics metrics,
    TimeProvider timeProvider,
    ILogger<SearchSupportResources> logger)
{
    public const int DefaultLimit = 5;

    /// <summary>A search the caller typed: screened when the caller is a participant, then searched.</summary>
    public async Task<SupportSearchOutcome> HandleAsync(SearchSupportResourcesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (notice, recording) = Screen(request.Text);
        try
        {
            var found = await SearchAsync(request, cancellationToken);
            return new SupportSearchOutcome(
                notice is not null && found.IsSuccess ? Result.Success(found.Value with { SafetyNotice = notice }) : found, notice);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (notice is not null)
        {
            // The notice must never depend on the database (D-138). Only the type is logged: a message can quote rows.
            LogFaulted(logger, exception.GetType().Name);
            return new SupportSearchOutcome(Result.Failure<SupportResourceDiscoveryResponse>(SupportResourceSearchErrors.Faulted), notice);
        }
        finally
        {
            // The recording never throws for a token it was not given; waiting keeps it inside the request.
            await recording;
        }
    }

    /// <summary>
    /// The search alone, never screened: for the assistant's directory tool, whose text is the model's words for the
    /// need, not the person's (the chat screens the person's own message).
    /// </summary>
    public async Task<Result<SupportResourceDiscoveryResponse>> SearchAsync(
        SearchSupportResourcesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = new SupportResourceQuery(
            request.Text,
            SupportResourceQueryText.Parse(request.Text),
            request.Categories ?? [],
            request.Audiences ?? [],
            SupportResourceAccess.VisibilitiesFor(currentUser.Roles),
            request.Limit ?? DefaultLimit);

        var started = timeProvider.GetTimestamp();
        var discovery = await assistant.DiscoverAsync(query, cancellationToken);
        metrics.SearchCompleted(discovery.Mode, discovery.Outcome, discovery.Fallback, timeProvider.GetElapsedTime(started));

        return Result.Success(new SupportResourceDiscoveryResponse(
            discovery.Outcome,
            discovery.Mode,
            discovery.Fallback,
            DiscoveryNotice.InformationOnly,
            discovery.Matches.Select((resource, index) => SupportResourceMatchResponse.From(resource, index + 1)).ToList(),
            null));
    }

    /// <summary>
    /// <c>crisis_support</c> and the recording it started when a participant's text matched; otherwise null and nothing.
    /// </summary>
    private (string? Notice, Task Recording) Screen(string? text)
    {
        if (!currentUser.Roles.Contains(Roles.Participant) || screen.Screen(text) is not { } match)
        {
            return (null, Task.CompletedTask);
        }

        // The app's language is not sent with a search, so the alert names none. Never the request's token: a person who
        // leaves the screen still raises the alert; the recorder bounds itself.
        var recording = recorder.RecordForCallerAsync(
            new SafetySignal(SafetyAlertSource.SupportSearch, match.Concern, SafetyDetector.PhraseScreen, match.RuleCode, null),
            CancellationToken.None);
        return (SafetyNotices.CrisisSupport, recording);
    }

    [LoggerMessage(EventId = 1701, Level = LogLevel.Error, Message = "A directory search stopped on a fault of type {ExceptionType} after a crisis signal.")]
    private static partial void LogFaulted(ILogger logger, string exceptionType);
}
