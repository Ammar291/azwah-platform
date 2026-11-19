using FamilyResilience.Application.Assistant;

namespace FamilyResilience.UnitTests.Application.Assistant;

public sealed class LiveConsultantSessionsTests
{
    [Fact]
    public void Claiming_again_for_a_subject_replaces_only_that_subjects_earlier_slot()
    {
        var live = new LiveConsultantSessions();

        using var first = live.Claim("subject-a");
        using var other = live.Claim("subject-b");
        using var second = live.Claim("subject-a");

        Assert.True(first.Replaced.IsCancellationRequested);
        Assert.False(other.Replaced.IsCancellationRequested);
        Assert.False(second.Replaced.IsCancellationRequested);
        Assert.Equal(2, live.Count);
    }

    [Fact]
    public void Freeing_a_replaced_slot_keeps_the_newer_one()
    {
        var live = new LiveConsultantSessions();
        var first = live.Claim("subject-a");
        using var second = live.Claim("subject-a");

        first.Dispose();
        first.Dispose();

        Assert.Equal(1, live.Count);
        Assert.False(second.Replaced.IsCancellationRequested);
        second.Dispose();
        Assert.Equal(0, live.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_slot_needs_a_subject(string subject) =>
        Assert.ThrowsAny<ArgumentException>(() => new LiveConsultantSessions().Claim(subject));
}
