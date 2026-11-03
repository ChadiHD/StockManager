using FluentAssertions;
using SMPortal.Services;
using Xunit;

namespace SMPortal.Tests;

// Exports carry what registrants typed, and a spreadsheet evaluates a cell that looks like a
// formula even when it is quoted.
public class CsvTests
{
    [Theory]
    [InlineData("=HYPERLINK(\"https://evil.example\",\"x\")")]
    [InlineData("+1+2")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1)")]
    [InlineData("\tdata")]
    public void ACellThatWouldRunIsWrittenAsText(string cell) =>
        Row(cell).Should().StartWith("\"'");

    [Theory]
    [InlineData("Acme Ltd", "\"Acme Ltd\"")]
    [InlineData("-1250.50", "\"-1250.50\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    public void EverythingElseIsUnchanged(string cell, string written) =>
        Row(cell).Should().Be(written);

    private static string Row(string cell) =>
        Csv.Build(["h"], [[cell]]).Split("\r\n")[1];
}
