using FamilyResilience.Domain.Common;

namespace FamilyResilience.UnitTests.Domain.Common;

public sealed class ApprovalTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Valid_approval_is_kept()
    {
        var result = Approval.Create("approver-subject", Now, "Programme board minutes 2026-10-01");

        Assert.True(result.IsSuccess);
        Assert.Equal("approver-subject", result.Value.ApprovedBy);
        Assert.Equal(Now, result.Value.ApprovedAt);
        Assert.Equal("Programme board minutes 2026-10-01", result.Value.Source);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Approver_is_required(string? approvedBy) =>
        Assert.Equal(CommonErrors.ApproverInvalid, Approval.Create(approvedBy, Now, "source").Error);

    [Fact]
    public void Approver_longer_than_200_characters_is_rejected() =>
        Assert.Equal(CommonErrors.ApproverInvalid, Approval.Create(new string('a', 201), Now, "source").Error);

    [Fact]
    public void Approval_time_must_be_utc() =>
        Assert.Equal(
            CommonErrors.TimeNotUtc,
            Approval.Create("approver", Now.ToOffset(TimeSpan.FromHours(4)), "source").Error);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Source_is_required(string? source) =>
        Assert.Equal(CommonErrors.ApprovalSourceInvalid, Approval.Create("approver", Now, source).Error);

    [Fact]
    public void Source_longer_than_300_characters_is_rejected() =>
        Assert.Equal(CommonErrors.ApprovalSourceInvalid, Approval.Create("approver", Now, new string('s', 301)).Error);
}
