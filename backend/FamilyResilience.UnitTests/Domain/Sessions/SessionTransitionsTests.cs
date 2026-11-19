using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.UnitTests.Domain.Sessions;

public sealed class SessionTransitionsTests
{
    private static readonly HashSet<(SessionStatus, SessionStatus)> Expected =
    [
        (SessionStatus.InProgress, SessionStatus.Submitted),
        (SessionStatus.InProgress, SessionStatus.Abandoned),
        (SessionStatus.Submitted, SessionStatus.Scored),
        (SessionStatus.Scored, SessionStatus.Scored),
    ];

    [Fact]
    public void Exactly_the_documented_moves_are_allowed()
    {
        foreach (var from in Enum.GetValues<SessionStatus>())
        {
            foreach (var to in Enum.GetValues<SessionStatus>())
            {
                Assert.True(
                    Expected.Contains((from, to)) == SessionTransitions.IsAllowed(from, to),
                    $"{from} -> {to} should be {(Expected.Contains((from, to)) ? "allowed" : "refused")}.");
            }
        }
    }

    [Fact]
    public void A_submitted_session_never_returns_to_in_progress() =>
        Assert.False(SessionTransitions.IsAllowed(SessionStatus.Submitted, SessionStatus.InProgress));
}
