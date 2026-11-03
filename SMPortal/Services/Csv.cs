using System.Globalization;
using System.Text;

namespace SMPortal.Services;

public static class Csv
{
    public static string Build(string[] headers, IEnumerable<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.Append(Line(headers));
        foreach (var r in rows) sb.Append(Line(r));
        return sb.ToString();
    }

    private static string Line(string[] cells) => string.Join(",", cells.Select(Quote)) + "\r\n";
    private static string Quote(string s) => "\"" + Inert(s ?? "").Replace("\"", "\"\"") + "\"";

    // Company names and contacts are typed by registrants, and a spreadsheet runs a cell that
    // starts like a formula whether or not it is quoted: =HYPERLINK(...) in a company name
    // runs on the admin's machine when the export is opened. The apostrophe is the spreadsheet
    // convention for "this is text". A plain number is left alone, so a negative one still sums.
    private static string Inert(string s) =>
        s.Length > 0 && s[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
        && !decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out _)
            ? "'" + s
            : s;
}
