using System.Net;
using FamilyResilience.Api.RateLimiting;

namespace FamilyResilience.UnitTests.Api;

public sealed class RateLimitPartitionTests
{
    [Theory]
    [InlineData("203.0.113.9", "203.0.113.9")]
    [InlineData("::ffff:203.0.113.9", "203.0.113.9")]
    [InlineData("2001:db8:1234:5678:9abc:def0:1234:5678", "2001:db8:1234:5678::/64")]
    [InlineData("2001:db8:1234:5678::1", "2001:db8:1234:5678::/64")]
    public void Anonymous_callers_share_a_partition_per_ipv4_address_or_ipv6_64(string address, string key) =>
        Assert.Equal(key, RateLimitSetup.AddressKey(IPAddress.Parse(address)));

    [Fact]
    public void A_caller_without_an_address_shares_one_partition() =>
        Assert.Equal("unknown", RateLimitSetup.AddressKey(null));
}
