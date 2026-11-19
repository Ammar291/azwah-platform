using FamilyResilience.Application.Identity;
using FamilyResilience.Application.SupportResources;
using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.UnitTests.Application.SupportResources;

public sealed class SupportResourceAccessTests
{
    [Theory]
    [InlineData(Roles.SupportCoordinator, true)]
    [InlineData(Roles.SystemAdministrator, true)]
    [InlineData(Roles.Participant, false)]
    [InlineData(Roles.Researcher, false)]
    public void Staff_roles_also_see_staff_only_resources(string role, bool staff)
    {
        var visibilities = SupportResourceAccess.VisibilitiesFor([role]);

        Assert.Equal(staff, visibilities.Contains(SupportResourceVisibility.StaffOnly));
        Assert.Contains(SupportResourceVisibility.Everyone, visibilities);
    }

    [Fact]
    public void Without_roles_only_public_resources_are_visible() =>
        Assert.Equal([SupportResourceVisibility.Everyone], SupportResourceAccess.VisibilitiesFor([]));
}
