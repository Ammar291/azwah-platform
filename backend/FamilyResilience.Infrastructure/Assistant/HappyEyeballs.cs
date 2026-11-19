using System.Net;
using System.Net.Sockets;

namespace FamilyResilience.Infrastructure.Assistant;

/// <summary>
/// Connects to OpenAI by racing its addresses (RFC 8305, "Happy Eyeballs"), which .NET does not do by itself (D-121). .NET
/// tries addresses one after another, and on a network where IPv6 is advertised but does not reach OpenAI each IPv6
/// address hangs until the operating system gives up: measured 44 s before the first reply or live session, past the
/// 30 s limit. Here the families alternate, each next attempt starts 250 ms after the last or as soon as it fails, and the
/// first connection wins.
/// </summary>
public static class HappyEyeballs
{
    public static readonly TimeSpan AttemptDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>The handler both OpenAI adapters send through: racing connections, pooled for five minutes.</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        ConnectCallback = async (context, cancellationToken) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
            return await ConnectAsync(addresses, context.DnsEndPoint.Port, AttemptDelay, cancellationToken);
        },
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };

    /// <summary>
    /// The addresses with their families alternating, each family in the order given, IPv4 first: on the development
    /// network an IPv6 connection to OpenAI could open and then stall a live stream for 20 s (D-124), and every OpenAI
    /// address family carries IPv4.
    /// </summary>
    public static IReadOnlyList<IPAddress> Order(IReadOnlyList<IPAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        if (addresses.Count == 0)
        {
            return [];
        }

        var leading = addresses.Any(address => address.AddressFamily == AddressFamily.InterNetwork)
            ? AddressFamily.InterNetwork
            : addresses[0].AddressFamily;
        var first = addresses.Where(address => address.AddressFamily == leading).ToList();
        var second = addresses.Where(address => address.AddressFamily != leading).ToList();
        var ordered = new List<IPAddress>(addresses.Count);
        for (var i = 0; i < Math.Max(first.Count, second.Count); i++)
        {
            if (i < first.Count)
            {
                ordered.Add(first[i]);
            }

            if (i < second.Count)
            {
                ordered.Add(second[i]);
            }
        }

        return ordered;
    }

    public static async Task<Stream> ConnectAsync(
        IReadOnlyList<IPAddress> addresses, int port, TimeSpan attemptDelay, CancellationToken cancellationToken)
    {
        var ordered = Order(addresses);
        using var race = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pending = new List<Task<Socket>>();
        Exception? failure = null;
        var next = 0;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (next < ordered.Count)
                {
                    pending.Add(ConnectOneAsync(ordered[next++], port, race.Token));
                }

                if (pending.Count == 0)
                {
                    throw failure ?? new SocketException((int)SocketError.HostNotFound);
                }

                var wait = next < ordered.Count ? Task.Delay(attemptDelay, race.Token) : Task.Delay(Timeout.Infinite, race.Token);
                var finished = await Task.WhenAny([.. pending, wait]);
                if (finished == wait)
                {
                    continue;
                }

                var attempt = (Task<Socket>)finished;
                pending.Remove(attempt);
                if (attempt.IsCompletedSuccessfully)
                {
                    return new NetworkStream(await attempt, ownsSocket: true);
                }

                failure = attempt.Exception?.InnerException ?? failure;
            }
        }
        finally
        {
            await race.CancelAsync();

            // Attempts still running lose: each closes its socket if it connects after all.
            foreach (var loser in pending)
            {
                _ = loser.ContinueWith(
                    task =>
                    {
                        if (task.IsCompletedSuccessfully)
                        {
                            task.Result.Dispose();
                        }
                        else
                        {
                            _ = task.Exception;
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
            }
        }
    }

    private static async Task<Socket> ConnectOneAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
