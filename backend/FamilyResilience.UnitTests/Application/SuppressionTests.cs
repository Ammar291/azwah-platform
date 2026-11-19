using FamilyResilience.Application.Analytics;

namespace FamilyResilience.UnitTests.Application;

public sealed class SuppressionTests
{
    /// <summary>Cells where every counted thing belongs to a different member, so the people are the counts.</summary>
    private static IReadOnlyList<bool> OnePersonPerCount(IReadOnlyList<int> counts, int minimum) =>
        Suppression.Apply(counts, counts, minimum);

    [Fact]
    public void Counts_at_or_above_the_minimum_and_zeros_are_shown() =>
        Assert.Equal([false, false, false], OnePersonPerCount([10, 0, 25], 10));

    [Fact]
    public void Two_small_cells_are_hidden_and_need_no_complement() =>
        Assert.Equal([true, true, false], OnePersonPerCount([3, 9, 40], 10));

    [Fact]
    public void A_single_small_cell_hides_the_next_smallest_non_zero_cell_too() =>
        Assert.Equal([false, true, false, true], OnePersonPerCount([30, 12, 0, 2], 10));

    [Fact]
    public void A_single_small_cell_with_no_partner_to_hide_hides_the_whole_group() =>
        Assert.Equal([true, true, true, true], OnePersonPerCount([0, 4, 0, 0], 10));

    [Fact]
    public void A_single_large_cell_among_zeros_is_shown() =>
        Assert.Equal([false, false, false], OnePersonPerCount([0, 40, 0], 10));

    [Fact]
    public void Ties_for_the_complement_hide_the_first_one() =>
        Assert.Equal([true, true, false], OnePersonPerCount([1, 15, 15], 10));

    [Fact]
    public void A_minimum_of_one_hides_nothing() =>
        Assert.Equal([false, false], OnePersonPerCount([1, 2], 1));

    [Fact]
    public void An_empty_group_hides_nothing() =>
        Assert.Empty(Suppression.Apply([], [], 10));

    // D-58 suppresses cells covering fewer than 10 people. Twelve assignments held by four members are four people.
    [Fact]
    public void A_count_at_the_minimum_is_hidden_when_fewer_people_than_the_minimum_are_behind_it() =>
        Assert.Equal([true, true, false], Suppression.Apply([12, 11, 40], [4, 9, 40], 10));

    [Fact]
    public void Exactly_the_minimum_number_of_people_is_shown_whatever_the_count() =>
        Assert.Equal([false, false], Suppression.Apply([30, 10], [10, 10], 10));

    [Fact]
    public void One_cell_with_too_few_people_hides_the_cell_with_the_next_fewest_people_not_the_next_smallest_count()
    {
        // The second cell has the smallest count shown, the third the fewest people among the cells still shown.
        var hidden = Suppression.Apply(counts: [12, 20, 36, 50], members: [4, 20, 12, 50], minimum: 10);

        Assert.Equal([true, false, true, false], hidden);
    }

    [Fact]
    public void Cells_covering_the_same_number_of_people_break_the_complement_tie_by_count_then_position() =>
        Assert.Equal([true, false, true, false], Suppression.Apply([12, 30, 15, 15], [4, 15, 15, 15], 10));

    [Fact]
    public void One_cell_with_too_few_people_among_zeros_hides_the_whole_group() =>
        Assert.Equal([true, true, true], Suppression.Apply([0, 12, 0], [0, 4, 0], 10));

    [Fact]
    public void Every_cell_needs_its_member_count() =>
        Assert.Throws<ArgumentException>(() => Suppression.Apply([12, 3], [4], 10));
}
