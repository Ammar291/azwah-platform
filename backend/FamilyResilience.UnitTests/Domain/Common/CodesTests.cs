using FamilyResilience.Domain.Common;

namespace FamilyResilience.UnitTests.Domain.Common;

public sealed class CodesTests
{
    [Theory]
    [InlineData("household_assessment")]
    [InlineData("tier_1")]
    [InlineData("a")]
    public void Snake_codes_are_accepted(string code) => Assert.True(Codes.IsSnakeCode(code));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Household")]
    [InlineData("1st")]
    [InlineData("dev-wave")]
    [InlineData("code\n")]
    public void Other_text_is_not_a_snake_code(string? code) => Assert.False(Codes.IsSnakeCode(code));

    [Fact]
    public void Snake_code_longer_than_64_characters_is_rejected()
    {
        Assert.True(Codes.IsSnakeCode(new string('a', 64)));
        Assert.False(Codes.IsSnakeCode(new string('a', 65)));
    }

    [Theory]
    [InlineData("A1")]
    [InlineData("B.2")]
    [InlineData("o-3")]
    [InlineData("S1_x")]
    public void Content_codes_are_accepted(string code) => Assert.True(Codes.IsContentCode(code));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-A")]
    [InlineData("A 1")]
    [InlineData("A1\n")]
    public void Other_text_is_not_a_content_code(string? code) => Assert.False(Codes.IsContentCode(code));

    [Fact]
    public void Content_code_longer_than_32_characters_is_rejected()
    {
        Assert.True(Codes.IsContentCode(new string('A', 32)));
        Assert.False(Codes.IsContentCode(new string('A', 33)));
    }
}
