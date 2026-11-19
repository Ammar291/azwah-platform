using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FamilyResilience.Infrastructure.Assistant;

namespace FamilyResilience.UnitTests.Infrastructure;

/// <summary>Connections to OpenAI race their addresses, so one dead route costs a moment, not a time-out (D-121).</summary>
public sealed class HappyEyeballsTests
{
    [Fact]
    public void Addresses_alternate_between_families_ipv4_first_each_family_in_the_order_given()
    {
        IPAddress[] resolved = [IPAddress.Parse("2001:db8::1"), IPAddress.Parse("2001:db8::2"), IPAddress.Parse("192.0.2.1"), IPAddress.Parse("192.0.2.2")];

        Assert.Equal(
            ["192.0.2.1", "2001:db8::1", "192.0.2.2", "2001:db8::2"],
            HappyEyeballs.Order(resolved).Select(address => address.ToString()));
    }

    [Fact]
    public void Ipv6_alone_keeps_its_order()
    {
        IPAddress[] resolved = [IPAddress.Parse("2001:db8::2"), IPAddress.Parse("2001:db8::1")];

        Assert.Equal(resolved, HappyEyeballs.Order(resolved));
    }

    [Fact]
    public void One_family_keeps_its_order()
    {
        IPAddress[] resolved = [IPAddress.Parse("192.0.2.2"), IPAddress.Parse("192.0.2.1")];

        Assert.Equal(resolved, HappyEyeballs.Order(resolved));
    }

    [Fact]
    public async Task An_address_that_does_not_answer_gives_way_to_the_next_after_the_attempt_delay()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var watch = Stopwatch.StartNew();

        // 192.0.2.1 is reserved for documentation and never answers: it either hangs or fails at once.
        await using var stream = await HappyEyeballs.ConnectAsync(
            [IPAddress.Parse("192.0.2.1"), IPAddress.Loopback], port, TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);

        Assert.True(stream.CanWrite);
        Assert.InRange(watch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task When_every_address_fails_the_last_failure_is_thrown()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        await Assert.ThrowsAsync<SocketException>(async () => await HappyEyeballs.ConnectAsync(
            [IPAddress.Loopback], port, TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_callers_cancellation_stops_the_race()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await HappyEyeballs.ConnectAsync(
            [IPAddress.Parse("192.0.2.1")], 443, TimeSpan.FromMilliseconds(250), cancelled.Token));
    }
}
