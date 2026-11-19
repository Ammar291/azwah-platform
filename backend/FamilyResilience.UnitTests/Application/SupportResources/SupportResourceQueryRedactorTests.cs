using FamilyResilience.Application.SupportResources;

namespace FamilyResilience.UnitTests.Application.SupportResources;

public sealed class SupportResourceQueryRedactorTests
{
    [Fact]
    public void Redaction_removes_contact_details_and_numbers()
    {
        var text = "help with debt, mail a.b@example.org or see https://example.org/x and www.example.org " +
                   "id 784-1990-1234567-1 call 0501234567 or ٠٥٠١٢٣ and ۰۵۰۱ now";

        var redacted = SupportResourceQueryRedactor.Redact(text);

        Assert.Equal("help with debt, mail or see and id call or and now", redacted);
    }

    [Fact]
    public void Redaction_removes_digits_of_every_script()
    {
        Assert.Equal("call or now", SupportResourceQueryRedactor.Redact("call ０５０１２３ or ०५० now"));
        Assert.Equal(string.Empty, SupportResourceQueryRedactor.Redact("０５０１２３"));
    }

    [Fact]
    public void Redaction_removes_an_emirates_id_with_its_separators()
    {
        Assert.Equal("id ok", SupportResourceQueryRedactor.Redact("id 784-1990-1234567-1 ok"));
        Assert.Equal("id ok", SupportResourceQueryRedactor.Redact("id 784 ١٩٩٠ ١٢٣٤٥٦٧ ١ ok"));
        Assert.Equal("id ok", SupportResourceQueryRedactor.Redact("id 784-１９９０-１２３４５６７-１ ok"));
    }

    [Fact]
    public void Redaction_collapses_space_and_caps_length()
    {
        Assert.Equal("a b c", SupportResourceQueryRedactor.Redact("  a \t\n  b   c  "));

        var capped = SupportResourceQueryRedactor.Redact(new string('x', 500));

        Assert.Equal(200, capped.Length);
    }

    [Fact]
    public void Redaction_of_nothing_is_empty()
    {
        Assert.Equal(string.Empty, SupportResourceQueryRedactor.Redact(null));
        Assert.Equal(string.Empty, SupportResourceQueryRedactor.Redact("   "));
        Assert.Equal(string.Empty, SupportResourceQueryRedactor.Redact("123 ٤٥٦"));
    }

    [Fact]
    public void Request_and_query_strings_never_contain_the_search_text()
    {
        const string text = "secret-words-here";
        var request = new SearchSupportResourcesRequest(text, ["a"], ["b"], 5);
        var query = new SupportResourceQuery(text, SupportResourceQueryText.Parse(text), ["a"], [], [], 5);

        Assert.DoesNotContain("secret", request.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret", query.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Model_request_and_reply_strings_print_only_lengths_and_counts()
    {
        const string instructions = "secret instructions";
        const string data = "{\"query\":\"secret words\"}";
        const string schema = "{\"secret\":true}";
        const string json = "{\"selected\":[\"secret\"]}";
        var request = new LanguageModelRequest(instructions, data, schema, 200);
        var reply = new LanguageModelReply(json, 120, 7);

        Assert.DoesNotContain("secret", request.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret", reply.ToString(), StringComparison.Ordinal);
        Assert.Equal(
            $"LanguageModelRequest {{ InstructionsLength = {instructions.Length}, DataLength = {data.Length}, JsonSchemaLength = {schema.Length}, MaxOutputTokens = 200 }}",
            request.ToString());
        Assert.Equal($"LanguageModelReply {{ JsonLength = {json.Length}, InputTokens = 120, OutputTokens = 7 }}", reply.ToString());
    }
}
