using System.Net.WebSockets;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Domain.Safeguarding;
using FluentValidation;

namespace FamilyResilience.Api.Assistant;

/// <summary>
/// Relays one live consultant session between the app's WebSocket and the voice model (protocol version 2, D-124). It
/// accepts the socket, waits up to <see cref="AssistantLimits.ConsultantStartWait"/> for the app's <c>start</c> message
/// (the memory and latest messages to carry on from), asks <see cref="StartConsultantSession"/> to open the session
/// (context, slot, tools and audit live there), then sends <c>ready</c> and relays microphone audio to the model and the
/// model's audio, captions, turn ends and offered screens to the app, until the app ends or closes, the model ends or
/// fails, the session reaches its time limit or goes without audio from the app, or the same subject starts another
/// session. The session is a <see cref="ContinuingConsultantSession"/>: when OpenAI's filter stops a model session the
/// call carries on in a new one, so a provider filter is never a reason for this relay to end the call (D-141). A crisis
/// signal in the call (a final line from the person matching the phrase screen, or the session's
/// <see cref="ConsultantSafetyFlag"/>) is recorded for staff and tells the app with the safety message
/// (<see cref="ConsultantSafetyWatch"/>, D-135, D-138); the line the person was still saying when the call ended is
/// screened and recorded too. Recording runs beside the relay and never holds it; the request waits for it (bounded)
/// once the app's socket, the model session and the slot are closed. The server keeps none of it, and logs and counts
/// only how the session ended, as a fixed code, after that wait.
/// </summary>
public sealed partial class ConsultantRelay(
    StartConsultantSession start,
    IValidator<ConsultantStartMessage> startValidator,
    CrisisSignalScreen screen,
    ISafetySignalRecorder recorder,
    AssistantLimits limits,
    AssistantMetrics metrics,
    TimeProvider timeProvider,
    ILogger<ConsultantRelay> logger)
{
    private enum Ending
    {
        /// <summary>The connection went away; nothing more can be sent.</summary>
        Aborted,

        /// <summary>The app sent a close frame.</summary>
        ClientClosed,

        /// <summary>The app sent <c>{"type":"end"}</c>.</summary>
        ClientEnded,

        /// <summary>The app sent a message over 64 KB.</summary>
        FrameTooBig,

        /// <summary>The app's first message was not a valid <c>start</c>, or did not come in time.</summary>
        BadStart,

        /// <summary>No model is configured.</summary>
        Unavailable,

        /// <summary>The model ended the session itself.</summary>
        ModelEnded,

        /// <summary>The model failed to open, failed while running, or a fault stopped the relay.</summary>
        ModelFailed,

        /// <summary>The session reached <see cref="AssistantLimits.ConsultantSessionLimit"/>.</summary>
        TooLong,

        /// <summary>No audio came from the app for <see cref="AssistantLimits.ConsultantIdleLimit"/>.</summary>
        Idle,

        /// <summary>The same subject started another live session.</summary>
        Replaced,

        /// <summary>A relay loop stopped because the session was already stopping; the real reason is in the tokens.</summary>
        Stopped,
    }

    public async Task RunAsync(HttpContext context, ConsultantStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        var aborted = context.RequestAborted;
        var began = timeProvider.GetTimestamp();
        var safety = new ConsultantSafetyWatch(screen, recorder, request.Language!, timeProvider);
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var channel = new ConsultantChannel(socket, aborted);
        var ending = Ending.Aborted;
        try
        {
            var (startMessage, pending, early) = await ReadStartAsync(channel);
            if (startMessage is null)
            {
                ending = early ?? Ending.BadStart;
                await FinishAsync(channel, ending, pending);
                return;
            }

            var started = await start.HandleAsync(request, startMessage, aborted);
            if (started.IsFailure)
            {
                ending = started.Error == AssistantErrors.Unavailable ? Ending.Unavailable : Ending.ModelFailed;
                await FinishAsync(channel, ending, receiving: null);
                return;
            }

            await using var consultation = started.Value;
            ending = await RelayAsync(channel, consultation, safety, timeProvider.GetTimestamp(), aborted);
        }
        catch (OperationCanceledException) when (aborted.IsCancellationRequested)
        {
            ending = Ending.Aborted;
        }
        catch (Exception exception)
        {
            // After the upgrade no error page can be written: tell the app the session failed and close. Only the type
            // is logged, because a message could quote content.
            LogFault(logger, exception.GetType().Name);
            ending = Ending.ModelFailed;
            await FinishAsync(channel, ending, receiving: null);
        }
        finally
        {
            // The model session and the slot are released by now, so a slow database holds neither: an alert heard in
            // the call that is still being recorded is waited for (bounded), so ending the call never drops it. The
            // session's length leaves that wait out.
            var elapsed = timeProvider.GetElapsedTime(began);
            try
            {
                await safety.SettleAsync();
            }
            finally
            {
                LogEnded(logger, Code(ending));
                metrics.ConsultantEnded(Code(ending), elapsed);
            }
        }
    }

    /// <summary>
    /// The app's first message as a valid <c>start</c>; otherwise null, with the receive still running when it timed out
    /// (so closing waits on it rather than reading twice) or how the connection ended before a message came.
    /// </summary>
    private async Task<(ConsultantStartMessage? Start, Task? Pending, Ending? Early)> ReadStartAsync(ConsultantChannel channel)
    {
        var buffer = new byte[ConsultantProtocol.MaxFrameBytes];
        var receiving = channel.ReceiveAsync(buffer);
        if (!await CompletesWithinAsync(receiving, limits.ConsultantStartWait))
        {
            return (null, ObserveAsync(receiving), null);
        }

        WebSocketMessageType type;
        int count;
        bool tooBig;
        try
        {
            (type, count, tooBig) = await receiving;
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            return (null, null, Ending.Aborted);
        }

        if (type == WebSocketMessageType.Close)
        {
            return (null, null, Ending.ClientClosed);
        }

        if (tooBig)
        {
            return (null, null, Ending.FrameTooBig);
        }

        if (type != WebSocketMessageType.Text || ConsultantMessages.ReadStart(buffer.AsMemory(0, count)) is not { } message)
        {
            return (null, null, null);
        }

        return (await startValidator.ValidateAsync(message)).IsValid ? (message, null, null) : (null, null, null);
    }

    private async Task<Ending> RelayAsync(
        ConsultantChannel channel, LiveConsultation consultation, ConsultantSafetyWatch safety, long startedAt, CancellationToken aborted)
    {
        using var tooLong = new CancellationTokenSource(limits.ConsultantSessionLimit);
        using var idle = new CancellationTokenSource(limits.ConsultantIdleLimit);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(aborted, consultation.Replaced, tooLong.Token, idle.Token);
        if (!await channel.SendTextAsync(ConsultantMessages.Ready))
        {
            return Ending.Aborted;
        }

        var receiving = ReceiveAsync(channel, consultation.Session, idle, stop.Token);
        var forwarding = ForwardAsync(channel, consultation.Session, safety, startedAt, stop.Token);
        var stopped = WhenCancelledAsync(stop.Token);
        var first = await Task.WhenAny(receiving, forwarding, stopped);
        var ending = first == receiving ? await receiving : first == forwarding ? await forwarding : Ending.Stopped;
        if (ending == Ending.Stopped)
        {
            ending = aborted.IsCancellationRequested ? Ending.Aborted
                : consultation.Replaced.IsCancellationRequested ? Ending.Replaced
                : tooLong.IsCancellationRequested ? Ending.TooLong
                : Ending.Idle;
        }

        await stop.CancelAsync();
        await stopped;

        // The model's events stop before anything else is sent; a send stuck on an app that reads nothing ends it all.
        if (!await CompletesWithinAsync(forwarding, limits.ConsultantCloseTimeout))
        {
            channel.Abort();
            ending = Ending.Aborted;

            // Forwarding never throws and ends once the stuck send fails, so every event it read is observed first.
            await forwarding;
        }

        // Nothing more is read: what the person was still saying when the call ended is screened too.
        safety.ObserveCallEnd();
        await FinishAsync(channel, ending, receiving);
        await receiving;
        return ending;
    }

    /// <summary>
    /// Microphone audio to the model and the app's control messages, until the app ends or closes. Once the session is
    /// stopping, everything but the close frame is dropped. Never throws.
    /// </summary>
    private async Task<Ending> ReceiveAsync(
        ConsultantChannel channel, IVoiceConsultantSession session, CancellationTokenSource idle, CancellationToken stop)
    {
        var buffer = new byte[ConsultantProtocol.MaxFrameBytes];
        try
        {
            while (true)
            {
                var (type, count, tooBig) = await channel.ReceiveAsync(buffer);
                if (type == WebSocketMessageType.Close)
                {
                    return Ending.ClientClosed;
                }

                if (tooBig)
                {
                    return Ending.FrameTooBig;
                }

                if (stop.IsCancellationRequested)
                {
                    continue;
                }

                if (type == WebSocketMessageType.Text)
                {
                    if (ConsultantMessages.IsEnd(buffer.AsMemory(0, count)))
                    {
                        return Ending.ClientEnded;
                    }

                    continue;
                }

                if (count == 0)
                {
                    continue;
                }

                idle.CancelAfter(limits.ConsultantIdleLimit);
                try
                {
                    // A copy: the buffer is reused for the next frame while the model may still hold this one.
                    await session.SendAudioAsync(buffer.AsMemory(0, count).ToArray(), stop);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    // Stopping: the frame is dropped.
                }
                catch (Exception)
                {
                    // Anything the model's side throws, its own socket closing included, is the model failing: the
                    // app is told model_failed. Only the app's socket (above) ends a session as aborted.
                    return Ending.ModelFailed;
                }
            }
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            return Ending.Aborted;
        }
    }

    /// <summary>
    /// The model's events to the app, as they come, until the model ends or the session stops; after an event that is a
    /// crisis signal, the safety message and SOS that <paramref name="safety"/> asks for (a flag itself is never sent).
    /// Never throws.
    /// </summary>
    private async Task<Ending> ForwardAsync(
        ConsultantChannel channel, IVoiceConsultantSession session, ConsultantSafetyWatch safety, long startedAt, CancellationToken stop)
    {
        var heard = false;
        try
        {
            await foreach (var item in session.ReadEventsAsync(stop).WithCancellation(stop))
            {
                if (item is ConsultantAudio && !heard)
                {
                    heard = true;
                    metrics.ConsultantFirstAudio(timeProvider.GetElapsedTime(startedAt));
                }

                var sent = item switch
                {
                    ConsultantAudio audio => await channel.SendBinaryAsync(audio.Pcm),
                    ConsultantTranscript transcript => await channel.SendTextAsync(ConsultantMessages.Transcript(transcript)),
                    ConsultantTurnEnded => await channel.SendTextAsync(ConsultantMessages.TurnEnd),
                    ConsultantInterrupted => await channel.SendTextAsync(ConsultantMessages.Interrupted),
                    ConsultantAction action when AssistantScreens.IsKnown(action.Action) =>
                        await channel.SendTextAsync(ConsultantMessages.Action(action)),

                    // Never sent as it is: the safety watch below turns it into the safety message.
                    ConsultantSafetyFlag => true,
                    _ => true,
                };

                // Screened whether or not the app is still there: what the person said is recorded either way.
                var follow = safety.Observe(item);
                if (!sent)
                {
                    return Ending.Aborted;
                }

                foreach (var message in follow)
                {
                    if (!await channel.SendTextAsync(message))
                    {
                        return Ending.Aborted;
                    }
                }
            }

            return stop.IsCancellationRequested ? Ending.Stopped : Ending.ModelEnded;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return Ending.Stopped;
        }
        catch (Exception)
        {
            // Broad on purpose: any fault in the model's stream ends the session as model_failed, unlogged.
            return Ending.ModelFailed;
        }
    }

    /// <summary>
    /// Tells the app why the session ends (when the protocol has a message for it), sends the close frame, and waits a
    /// short time for the app's close frame before dropping the connection.
    /// </summary>
    private async Task FinishAsync(ConsultantChannel channel, Ending ending, Task? receiving)
    {
        switch (ending)
        {
            case Ending.Aborted:
                return;
            case Ending.ClientClosed:
                await channel.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "closed");
                return;
        }

        var message = ending switch
        {
            Ending.Unavailable => ConsultantMessages.Unavailable,
            Ending.ModelFailed => ConsultantMessages.Error("model_failed"),
            Ending.TooLong => ConsultantMessages.Error("too_long"),
            Ending.Idle => ConsultantMessages.Error("idle"),
            Ending.BadStart => ConsultantMessages.Error("bad_start"),
            _ => (ReadOnlyMemory<byte>?)null,
        };
        if (message is { } text)
        {
            await channel.SendTextAsync(text);
        }

        await channel.CloseOutputAsync(
            ending switch
            {
                Ending.ModelFailed => WebSocketCloseStatus.InternalServerError,
                Ending.FrameTooBig => WebSocketCloseStatus.MessageTooBig,
                Ending.BadStart => WebSocketCloseStatus.PolicyViolation,
                _ => WebSocketCloseStatus.NormalClosure,
            },
            Code(ending));

        var reading = receiving is { IsCompleted: false } ? receiving : channel.DrainAsync();
        if (!await CompletesWithinAsync(reading, limits.ConsultantCloseTimeout))
        {
            channel.Abort();
        }
    }

    /// <summary>Waits for a receive nobody will read, keeping its fault (a dropped connection) from going unobserved.</summary>
    private static async Task ObserveAsync(Task receiving)
    {
        try
        {
            await receiving;
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // The connection ended while the server waited for the start message.
        }
    }

    private static async Task<bool> CompletesWithinAsync(Task task, TimeSpan limit)
    {
        using var timer = new CancellationTokenSource();
        var finished = await Task.WhenAny(task, Task.Delay(limit, timer.Token));
        await timer.CancelAsync();
        return finished == task;
    }

    private static async Task WhenCancelledAsync(CancellationToken token)
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = token.Register(() => cancelled.TrySetResult());
        await cancelled.Task;
    }

    private static string Code(Ending ending) => ending switch
    {
        Ending.Aborted => "aborted",
        Ending.ClientClosed => "closed",
        Ending.ClientEnded => "ended",
        Ending.FrameTooBig => "too_big",
        Ending.BadStart => "bad_start",
        Ending.Unavailable => "unavailable",
        Ending.ModelEnded => "model_ended",
        Ending.ModelFailed => "model_failed",
        Ending.TooLong => "too_long",
        Ending.Idle => "idle",
        Ending.Replaced => "replaced",
        _ => "stopped",
    };

    [LoggerMessage(EventId = 1500, Level = LogLevel.Information, Message = "A live consultant session ended: {Ending}.")]
    private static partial void LogEnded(ILogger logger, string ending);

    [LoggerMessage(EventId = 1501, Level = LogLevel.Warning, Message = "A live consultant session stopped on a fault of type {ExceptionType}.")]
    private static partial void LogFault(ILogger logger, string exceptionType);
}
