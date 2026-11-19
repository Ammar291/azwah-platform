using System.Text.Json;
using CsCheck;
using FamilyResilience.Application.SupportResources;
using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.UnitTests.Application.SupportResources;

public sealed class SupportResourceSelectionGuardTests
{
    private static readonly SupportResource First = SyntheticSupportResources.Make("one");
    private static readonly SupportResource Second = SyntheticSupportResources.Make("two");

    private static readonly Dictionary<string, SupportResource> Handles = new() { ["r1"] = First, ["r2"] = Second };

    [Theory]
    [InlineData("{\"selected\":[]}")]
    [InlineData("{\"selected\":[\"r2\",\"r1\"]}")]
    [InlineData(" {\"selected\": [\"r1\"]} ")]
    public void Well_formed_selections_are_accepted(string json)
    {
        var verdict = SupportResourceSelectionGuard.Check(json, Handles, 5);

        Assert.Equal(SelectionVerdictKind.Accepted, verdict.Kind);
        using var document = JsonDocument.Parse(json);
        var expected = document.RootElement.GetProperty("selected").EnumerateArray()
            .Select(handle => Handles[handle.GetString()!]).ToArray();
        Assert.Equal(expected, verdict.Selected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Here are my picks: r1 and r2")]
    [InlineData("```json\n{\"selected\":[\"r1\"]}\n```")]
    [InlineData("{\"selected\":[\"r1\"]} trailing")]
    [InlineData("{\"selected\":[\"r1\"]}{\"selected\":[\"r2\"]}")]
    [InlineData("[\"r1\"]")]
    [InlineData("{\"selected\":[\"r1\"],\"phone\":\"800\"}")]
    [InlineData("{}")]
    [InlineData("{\"selected\":\"r1\"}")]
    [InlineData("{\"selected\":[1]}")]
    [InlineData("{\"selected\":[\"R1\"]}")]
    [InlineData("{\"selected\":[\"r100\"]}")]
    [InlineData("{\"selected\":[\"0b1c2d3e-0000-4000-8000-000000000000\"]}")]
    [InlineData("{\"selected\":[\"r1\",\"r1\"]}")]
    [InlineData("{\"selected\":[\"r1\",\"r2\",\"r1\"]}")]
    [InlineData("{\"selected\":[\"r1\"],}")]
    [InlineData("{\"selected\":[\"r1\"]} // note")]
    [InlineData("{\"selected\":[\"r1\",]}")]
    [InlineData("{\"selected\":null}")]
    [InlineData("{\"selected\":[[[[[]]]]]}")]
    [InlineData("{\"selected\":[\"\\uD800\"]}")]
    [InlineData("{\"selected\":[\"\\uDC00\"]}")]
    [InlineData("{\"\\uD800\":[]}")]
    [InlineData("{\"selected\":[\"r1\uD800\"]}")]
    public void Malformed_replies_are_invalid(string? json)
    {
        var verdict = SupportResourceSelectionGuard.Check(json, Handles, 5);

        Assert.Equal(SelectionVerdictKind.Invalid, verdict.Kind);
        Assert.Empty(verdict.Selected);
    }

    [Fact]
    public void More_than_the_limit_is_invalid()
    {
        var verdict = SupportResourceSelectionGuard.Check("{\"selected\":[\"r1\",\"r2\"]}", Handles, 1);

        Assert.Equal(SelectionVerdictKind.Invalid, verdict.Kind);
    }

    [Fact]
    public void An_overlong_reply_is_invalid()
    {
        var padded = "{\"selected\":[]}" + new string(' ', SupportResourceSelectionGuard.MaxReplyLength + 1 - 15);

        Assert.Equal(4097, padded.Length);
        Assert.Equal(SelectionVerdictKind.Invalid, SupportResourceSelectionGuard.Check(padded, Handles, 5).Kind);
    }

    [Fact]
    public void An_unknown_handle_is_ungrounded()
    {
        var verdict = SupportResourceSelectionGuard.Check("{\"selected\":[\"r9\"]}", Handles, 5);

        Assert.Equal(SelectionVerdictKind.Ungrounded, verdict.Kind);
        Assert.Empty(verdict.Selected);
    }

    [Fact]
    public void The_guard_never_throws_and_accepts_only_known_handles()
    {
        var fragment = Gen.OneOf(
            Gen.String[0, 6],
            Gen.OneOfConst(
                "{", "}", "[", "]", ",", ":", "\"", "\\", "\\u", "\\uD800", "\\uDC00", "\\uD83D\\uDE00", "\uD800", "\uDC00",
                "selected", "\"selected\"", "\"r1\"", "\"r2\"", "\"r9\"", "r1", "//", "/*", " ", "\n", "null", "1"));
        var text = fragment.List[0, 12].Select(parts => string.Concat(parts));
        var mutated = from valid in Gen.OneOfConst("{\"selected\":[\"r1\",\"r2\"]}", "{\"selected\":[]}")
                      from position in Gen.Int[0, valid.Length]
                      from insert in fragment
                      select valid.Insert(position, insert);
        var replies = Gen.OneOf(text, mutated);

        replies.Sample(reply =>
        {
            var verdict = SupportResourceSelectionGuard.Check(reply, Handles, 2);

            if (verdict.Kind != SelectionVerdictKind.Accepted)
            {
                Assert.Empty(verdict.Selected);
                return;
            }

            Assert.All(verdict.Selected, resource => Assert.Contains(resource, Handles.Values));
            Assert.Equal(verdict.Selected.Count, verdict.Selected.Distinct().Count());
            Assert.True(verdict.Selected.Count <= 2);
        });
    }

    [Fact]
    public void Accepted_selections_are_always_known_handles_within_the_limit()
    {
        var item = Gen.OneOf(
            Gen.Const("r1"), Gen.Const("r2"), Gen.Const("r3"), Gen.Const("r12"), Gen.Const("R1"), Gen.Const("x"),
            Gen.String[0, 5]);
        var reply = from items in item.List[0, 6]
                    from limit in Gen.Int[0, 5]
                    select (Json: JsonSerializer.Serialize(new { selected = items }), Limit: limit);

        reply.Sample(pair =>
        {
            var verdict = SupportResourceSelectionGuard.Check(pair.Json, Handles, pair.Limit);

            if (verdict.Kind != SelectionVerdictKind.Accepted)
            {
                Assert.Empty(verdict.Selected);
                return;
            }

            Assert.All(verdict.Selected, resource => Assert.Contains(resource, Handles.Values));
            Assert.Equal(verdict.Selected.Count, verdict.Selected.Distinct().Count());
            Assert.True(verdict.Selected.Count <= pair.Limit);
        });
    }
}
