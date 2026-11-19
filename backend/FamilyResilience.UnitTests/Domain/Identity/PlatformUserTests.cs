using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Identity;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.Identity;

public sealed class PlatformUserTests
{
    [Fact]
    public void Register_keeps_the_subject_and_time()
    {
        var user = Ok(PlatformUser.Register("subject-1", Now));

        Assert.Equal(("subject-1", Now), (user.Subject, user.CreatedAt));
        Assert.Equal(7, user.Id.Value.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_refuses_a_missing_subject(string? subject) =>
        Assert.Equal(IdentityErrors.SubjectInvalid, PlatformUser.Register(subject, Now).Error);

    [Fact]
    public void Register_refuses_a_subject_longer_than_200_characters()
    {
        Assert.True(PlatformUser.Register(new string('s', 200), Now).IsSuccess);
        Assert.Equal(IdentityErrors.SubjectInvalid, PlatformUser.Register(new string('s', 201), Now).Error);
    }

    [Fact]
    public void Register_refuses_a_time_that_is_not_utc() =>
        Assert.Equal(CommonErrors.TimeNotUtc, PlatformUser.Register("subject-1", Now.ToOffset(TimeSpan.FromHours(4))).Error);
}
