using FamilyResilience.Application.Identity;

namespace FamilyResilience.UnitTests.Application;

public sealed class RoleChangeTests
{
    [Fact]
    public void First_observation_adds_every_known_role()
    {
        var change = RoleChange.Between([], ["researcher", "participant"]);

        Assert.Equal(["participant", "researcher"], change.Added);
        Assert.Empty(change.Removed);
        Assert.False(change.IsEmpty);
    }

    [Fact]
    public void Unknown_roles_are_ignored()
    {
        Assert.Equal(["researcher"], RoleChange.Known(["Researcher", "root", "researcher", "researcher", ""]));
        Assert.True(RoleChange.Between([], ["superuser"]).IsEmpty);
    }

    [Fact]
    public void A_changed_set_lists_added_and_removed_roles_sorted()
    {
        var change = RoleChange.Between(
            ["researcher", "program_administrator"], ["system_administrator", "researcher", "support_coordinator"]);

        Assert.Equal(["support_coordinator", "system_administrator"], change.Added);
        Assert.Equal(["program_administrator"], change.Removed);
    }

    [Fact]
    public void The_same_set_in_another_order_changes_nothing() =>
        Assert.True(RoleChange.Between(["researcher", "participant"], ["participant", "researcher"]).IsEmpty);
}
