using System.Text.Json;
using FamilyResilience.Application.Questionnaires;

namespace FamilyResilience.UnitTests.Application;

public sealed class ContentJsonTests
{
    [Fact]
    public void A_duplicate_key_is_refused() =>
        Assert.ThrowsAny<JsonException>(() =>
            JsonSerializer.Deserialize<TextDto>("""{"ar":null,"en":"a","en":"b"}""", ContentJson.Options));

    [Fact]
    public void A_key_that_differs_only_in_case_is_refused() =>
        Assert.ThrowsAny<JsonException>(() =>
            JsonSerializer.Deserialize<TextDto>("""{"ar":null,"En":"a"}""", ContentJson.Options));

    [Fact]
    public void A_number_written_as_a_string_is_refused() =>
        Assert.ThrowsAny<JsonException>(() =>
            JsonSerializer.Deserialize<ContentOptionDto>(
                """{"code":"5","displayOrder":5,"value":"5","isNonSubstantive":false,"label":{"ar":null,"en":"x"}}""",
                ContentJson.Options));
}
