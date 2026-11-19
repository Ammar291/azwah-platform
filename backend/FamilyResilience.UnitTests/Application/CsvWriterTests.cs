using System.Text;
using FamilyResilience.Application.Analytics;

namespace FamilyResilience.UnitTests.Application;

public sealed class CsvWriterTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("band_low", "band_low")]
    [InlineData("31.5", "31.5")]
    [InlineData("-2", "-2")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("+1 phone", "'+1 phone")]
    [InlineData("-cmd", "'-cmd")]
    [InlineData("@user", "'@user")]
    public void Cells_are_quoted_when_needed_and_never_start_a_formula(string? cell, string expected) =>
        Assert.Equal(expected, CsvWriter.Escape(cell));

    [Fact]
    public async Task A_file_is_a_header_and_rows_ending_in_crlf_in_utf8_without_a_bom()
    {
        using var stream = new MemoryStream();

        await CsvWriter.WriteAsync(stream, ["tier_code", "count"], [["band_low", "3"], ["band,high", null]], TestContext.Current.CancellationToken);

        var bytes = stream.ToArray();
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal("tier_code,count\r\nband_low,3\r\n\"band,high\",\r\n", Encoding.UTF8.GetString(bytes));
    }
}
