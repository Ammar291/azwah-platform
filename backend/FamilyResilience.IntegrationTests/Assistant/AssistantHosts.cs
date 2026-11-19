using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FamilyResilience.IntegrationTests.Assistant;

/// <summary>Test hosts with configured fake models, sharing the base factory's database, and their clients.</summary>
internal static class AssistantHosts
{
    /// <summary>Short limits, so the time-limit paths of the live consultant run in a test.</summary>
    public static AssistantLimits QuickLimits(TimeSpan? session = null, TimeSpan? idle = null) =>
        AssistantLimits.Default with
        {
            ConsultantSessionLimit = session ?? TimeSpan.FromMinutes(1),
            ConsultantIdleLimit = idle ?? TimeSpan.FromMinutes(1),
            ConsultantCloseTimeout = TimeSpan.FromSeconds(2),
            ConsultantStartWait = TimeSpan.FromSeconds(1),
        };

    /// <summary>The app's first message on a call with nothing to carry on from (protocol version 2).</summary>
    public const string FreshStart = """{"type":"start","memory":null,"messages":[]}""";

    public static WebApplicationFactory<Program> With(
        this WebApplicationFactory<Program> factory,
        IAssistantModel? assistant = null,
        IVoiceConsultantModel? consultant = null,
        IScoreReportModel? report = null,
        ISupportKnowledgeGraph? graph = null,
        AssistantLimits? limits = null,
        ILoggerProvider? logs = null,
        IAssistantSafetyCheck? safety = null,
        ISafetySignalRecorder? recorder = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            if (logs is not null)
            {
                builder.ConfigureLogging(logging => logging.AddProvider(logs));
            }

            builder.ConfigureTestServices(services =>
            {
                if (recorder is not null)
                {
                    services.AddSingleton(recorder);
                }

                if (assistant is not null)
                {
                    services.AddSingleton(assistant);
                }

                if (safety is not null)
                {
                    services.AddSingleton(safety);
                }

                if (consultant is not null)
                {
                    services.AddSingleton(consultant);
                }

                if (report is not null)
                {
                    services.AddSingleton(report);
                }

                if (graph is not null)
                {
                    services.AddSingleton(graph);
                }

                services.AddSingleton(limits ?? QuickLimits());
            });
        });

    /// <summary>A client of <paramref name="host"/> signed in through the test scheme.</summary>
    public static HttpClient Client(this WebApplicationFactory<Program> host, string subject, string role = Roles.Participant)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, subject);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, role);
        return client;
    }

    /// <summary>
    /// Opens the live consultant's WebSocket and sends <paramref name="start"/> as the app does (null sends nothing); no
    /// subject means anonymous.
    /// </summary>
    public static async Task<WebSocket> ConnectLiveAsync(
        this WebApplicationFactory<Program> host,
        string? subject,
        CancellationToken cancellationToken,
        string role = Roles.Participant,
        string language = AssistantLanguages.English,
        string? start = FreshStart)
    {
        var client = host.Server.CreateWebSocketClient();
        client.ConfigureRequest = request =>
        {
            if (subject is not null)
            {
                request.Headers[TestAuthHandler.UserHeader] = subject;
                request.Headers[TestAuthHandler.RolesHeader] = role;
            }
        };
        var socket = await client.ConnectAsync(new Uri($"ws://localhost/api/v1/consultant/live?language={language}"), cancellationToken);
        if (start is not null)
        {
            await socket.SendTextAsync(start, cancellationToken);
        }

        return socket;
    }
}

/// <summary>One whole message from the server, or its close frame.</summary>
internal sealed record LiveFrame(WebSocketMessageType Type, byte[] Data, WebSocketCloseStatus? CloseStatus, string? CloseDescription)
{
    public JsonElement Json
    {
        get
        {
            using var document = JsonDocument.Parse(Data);
            return document.RootElement.Clone();
        }
    }

    public string? MessageType => Type == WebSocketMessageType.Text ? Json.GetProperty("type").GetString() : null;
}

internal static class LiveSockets
{
    /// <summary>The next whole message, within ten seconds. A close frame is answered, completing the close handshake.</summary>
    public static async Task<LiveFrame> ReceiveAsync(this WebSocket socket, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var buffer = new byte[256 * 1024];
        var count = 0;
        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, count, buffer.Length - count), timeout.Token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                if (socket.State == WebSocketState.CloseReceived)
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
                }

                return new LiveFrame(WebSocketMessageType.Close, [], result.CloseStatus, result.CloseStatusDescription);
            }

            count += result.Count;
            if (result.EndOfMessage)
            {
                return new LiveFrame(result.MessageType, buffer[..count], null, null);
            }
        }
    }

    /// <summary>Messages until a close frame, the close frame included.</summary>
    public static async Task<List<LiveFrame>> ReceiveUntilCloseAsync(this WebSocket socket, CancellationToken cancellationToken)
    {
        var frames = new List<LiveFrame>();
        while (true)
        {
            var frame = await socket.ReceiveAsync(cancellationToken);
            frames.Add(frame);
            if (frame.Type == WebSocketMessageType.Close)
            {
                return frames;
            }
        }
    }

    public static Task SendTextAsync(this WebSocket socket, string json, CancellationToken cancellationToken) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, cancellationToken);

    public static Task SendAudioAsync(this WebSocket socket, byte[] pcm, CancellationToken cancellationToken) =>
        socket.SendAsync(pcm, WebSocketMessageType.Binary, true, cancellationToken);
}
