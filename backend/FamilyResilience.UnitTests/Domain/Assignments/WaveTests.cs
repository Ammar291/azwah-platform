using FamilyResilience.Domain.Assignments;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.Assignments;

public sealed class WaveTests
{
    [Fact]
    public void Planned_wave_accepts_assignments_and_can_open_then_close()
    {
        var wave = Ok(Wave.Plan("wave_2027_spring", Now.AddDays(10), Now.AddDays(100), Now));

        Assert.Equal(WaveStatus.Planned, wave.Status);
        Assert.True(wave.AcceptsAssignments);
        Assert.True(wave.Open(Now).IsSuccess);
        Assert.Equal(AssignmentErrors.WaveNotPlanned, wave.Open(Now).Error);
        Assert.True(wave.Close(Now).IsSuccess);
        Assert.False(wave.AcceptsAssignments);
        Assert.Equal(AssignmentErrors.WaveAlreadyClosed, wave.Close(Now).Error);
    }

    [Fact]
    public void Wave_must_close_after_it_opens() =>
        Assert.Equal(AssignmentErrors.WaveWindowInvalid, Wave.Plan("wave", Now, Now, Now).Error);

    [Theory]
    [InlineData("dev-wave-1")]
    [InlineData("Wave1")]
    public void Wave_code_must_be_a_snake_code(string code) =>
        Assert.Equal(AssignmentErrors.WaveCodeInvalid, Wave.Plan(code, Now, Now.AddDays(1), Now).Error);
}
