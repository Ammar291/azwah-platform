using System.Text.Json;
using System.Text.Json.Serialization;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Households;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.UnitTests.Application;

public sealed class OptionalTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private static UpdateMemberRequest Read(string json) =>
        JsonSerializer.Deserialize<UpdateMemberRequest>(json, Options)!;

    [Fact]
    public void An_absent_field_is_not_set()
    {
        var patch = Read("""{"employmentStatus":"retired"}""");

        Assert.False(patch.RelationshipToHousehold.IsSet);
        Assert.Equal(HouseholdRelationship.Spouse, patch.RelationshipToHousehold.Or(HouseholdRelationship.Spouse));
        Assert.Equal(new Optional<EmploymentStatus?>(EmploymentStatus.Retired), patch.EmploymentStatus);
    }

    [Fact]
    public void An_explicit_null_clears_a_nullable_field()
    {
        var patch = Read("""{"relationshipToHousehold":null}""");

        Assert.True(patch.RelationshipToHousehold.IsSet);
        Assert.Null(patch.RelationshipToHousehold.Or(HouseholdRelationship.Spouse));
    }

    [Fact]
    public void A_null_for_a_value_that_cannot_be_null_is_malformed_and_names_the_field()
    {
        var error = Assert.Throws<JsonException>(() => Read("""{"birthYear":null}"""));

        Assert.Equal("$.birthYear", error.Path);
    }

    [Fact]
    public void A_wrong_type_names_the_field()
    {
        var error = Assert.Throws<JsonException>(() => Read("""{"isHeadOfHousehold":"yes"}"""));

        Assert.Equal("$.isHeadOfHousehold", error.Path);
    }
}
