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
    private static string Quote(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
}
