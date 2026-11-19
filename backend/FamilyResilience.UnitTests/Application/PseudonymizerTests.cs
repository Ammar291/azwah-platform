using FamilyResilience.Application.Analytics;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.UnitTests.Application;

public sealed class PseudonymizerTests
{
    [Fact]
    public void One_export_gives_one_person_one_pseudonym()
    {
        using var pseudonyms = new Pseudonymizer();
        var member = FamilyMemberId.New();

        Assert.Equal(pseudonyms.Member(member), pseudonyms.Member(member));
        Assert.NotEqual(pseudonyms.Member(member), pseudonyms.Member(FamilyMemberId.New()));
    }

    [Fact]
    public void Two_exports_cannot_be_linked_through_their_pseudonyms()
    {
        using var first = new Pseudonymizer();
        using var second = new Pseudonymizer();
        var member = FamilyMemberId.New();

        Assert.NotEqual(first.Member(member), second.Member(member));
    }

    [Fact]
    public void A_member_and_a_household_with_the_same_value_get_different_pseudonyms()
    {
        using var pseudonyms = new Pseudonymizer();
        var value = Guid.NewGuid();

        Assert.NotEqual(pseudonyms.Member(FamilyMemberId.From(value)), pseudonyms.Household(HouseholdId.From(value)));
    }

    [Fact]
    public void A_pseudonym_is_32_lowercase_hex_characters_that_do_not_contain_the_id()
    {
        using var pseudonyms = new Pseudonymizer();
        var member = FamilyMemberId.New();

        var pseudonym = pseudonyms.Member(member);

        Assert.Matches("^[0-9a-f]{32}$", pseudonym);
        Assert.NotEqual(member.Value.ToString("N"), pseudonym);
    }

    [Fact]
    public void A_disposed_pseudonymizer_refuses_to_work()
    {
        var pseudonyms = new Pseudonymizer();
        pseudonyms.Dispose();

        Assert.Throws<ObjectDisposedException>(() => pseudonyms.Member(FamilyMemberId.New()));
    }
}
