using System.Text.RegularExpressions;
using FamilyResilience.Domain.Households;

namespace FamilyResilience.UnitTests.Domain.Households;

public sealed class HouseholdReferenceTests
{
    [Fact]
    public void Generated_reference_uses_the_hh_format_and_the_crockford_alphabet()
    {
        var reference = HouseholdReference.Generate();

        Assert.Equal(HouseholdReference.Length, reference.Value.Length);
        Assert.Matches(new Regex(@"^HH-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}\z"), reference.Value);
        Assert.Equal(reference, HouseholdReference.Parse(reference.Value).Value);
    }

    [Fact]
    public void Generated_references_are_random() =>
        Assert.Equal(200, Enumerable.Range(0, 200).Select(_ => HouseholdReference.Generate().Value).Distinct().Count());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hh-abcd-efgh")]
    [InlineData("HH-ABCD-EFGI")]
    [InlineData("HH-ABCD-EFGU")]
    [InlineData("HH-ABCDEFGH")]
    [InlineData("HH-ABCD-EFGH\n")]
    public void Anything_else_is_not_a_reference(string? value) =>
        Assert.Equal(HouseholdErrors.ReferenceInvalid, HouseholdReference.Parse(value).Error);
}
