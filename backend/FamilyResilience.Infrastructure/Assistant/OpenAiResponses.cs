using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FamilyResilience.Infrastructure.Assistant;

/// <summary>
/// The one way the text adapters (<see cref="OpenAiAssistantModel"/>, <see cref="OpenAiScoreReportModel"/>) post to
/// OpenAI's Responses API (D-112, D-119): the fixed address, the key in the <c>Authorization</c> header only, a fresh
/// request for each try, and at most one more try (<see cref="OpenAiRetry"/>). Each failure reaches the caller's log as a
/// fixed code only (<c>status_429</c>, <c>connection</c>, <c>unreadable</c>, or a retried one such as
/// <c>connection_retried</c>), never anything OpenAI sent back.
/// </summary>
internal static class OpenAiResponses
{
    public const string Address = "https://api.openai.com/v1/responses";

    /// <summary>The parsed answer of a successful call, or null when it failed (after its one more try, if allowed).</summary>
    public static async Task<JsonDocument?> PostAsync(
        HttpClient http, OpenAiSettings settings, string json, OpenAiRetry retry, Action<string> log, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(log);
        for (var attempt = 1; ; attempt++)
        {
            string retried;
            try
            {
                using var response = await SendOnceAsync(http, settings, json, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        return await JsonDocument.ParseAsync(
                            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                    }
                    catch (JsonException)
                    {
                        log("unreadable");
                        return null;
                    }
                }

                var status = (int)response.StatusCode;
                var busy = response.StatusCode == HttpStatusCode.TooManyRequests || status >= 500;
                if (!busy || !retry.AfterBusyAnswer || attempt == 2)
                {
                    log($"status_{status}");
                    return null;
                }

                retried = $"status_{status}";
            }
            catch (HttpRequestException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt == 2)
                {
                    log("connection");
                    return null;
                }

                retried = "connection";
            }

            log($"{retried}_retried");
            await Task.Delay(retry.Pause, cancellationToken);
        }
    }

    private static Task<HttpResponseMessage> SendOnceAsync(HttpClient http, OpenAiSettings settings, string json, CancellationToken cancellationToken)
    {
        // A request cannot be sent twice, so each try has its own. HttpClient disposes the content when it sends.
        var request = new HttpRequestMessage(HttpMethod.Post, Address)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        return http.SendAsync(request, cancellationToken);
    }
}
