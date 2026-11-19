using FamilyResilience.Application.Abstractions;

namespace FamilyResilience.UnitTests.Application;

public sealed class RequestHashTests
{
    [Fact]
    public void The_same_parts_give_the_same_lowercase_sha256()
    {
        var hash = RequestHash.Of("sessions.submit", "0198c6d6-0000-7000-8000-000000000001");

        Assert.Equal(hash, RequestHash.Of("sessions.submit", "0198c6d6-0000-7000-8000-000000000001"));
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void Moving_text_between_parts_changes_the_hash() =>
        Assert.NotEqual(RequestHash.Of("ab", "c"), RequestHash.Of("a", "bc"));

    [Fact]
    public void A_record_matches_only_its_own_operation_and_request()
    {
        var record = IdempotencyRecord.Create("subject", Guid.NewGuid(), "sessions.submit", Guid.NewGuid(), RequestHash.Of("x"), DateTimeOffset.UnixEpoch);

        Assert.True(record.Matches("sessions.submit", RequestHash.Of("x")));
        Assert.False(record.Matches("sessions.other", RequestHash.Of("x")));
        Assert.False(record.Matches("sessions.submit", RequestHash.Of("y")));
        Assert.Equal(TimeSpan.FromHours(24), record.ExpiresAt - record.CreatedAt);
    }
}
