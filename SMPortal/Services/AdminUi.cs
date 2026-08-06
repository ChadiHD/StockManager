using System.Globalization;

namespace SMPortal.Services;

// Formatting + presentation helpers. Colour decisions are returned as CSS
// class-name tokens (never inline styles), so the actual colours live in CSS.
public static class AdminUi
{
    private static readonly CultureInfo Ie = CultureInfo.GetCultureInfo("en-IE");

    public static string Symbol(string currency) => currency switch
    {
        "EUR" => "\u20ac",
        "GBP" => "\u00a3",
        "USD" => "$",
        _ => ""
    };

    public static string Money(decimal amount, string currency, int dp = 0) =>
        Symbol(currency) + amount.ToString("N" + dp, Ie);

    // status -> badge modifier class (see admin.css .badge--*)
    public static string StatusClass(string status) => status switch
    {
        "Approved" or "Accepted" or "Fulfilled" or "OK" or "Verified" or "In stock" => "badge--ok",
        "Priced" or "Processing" => "badge--info",
        "Pending" or "Awaiting payment" or "Awaiting review" or "Low" => "badge--warn",
        "Requested" or "Draft" => "badge--neutral",
        "Suspended" or "Rejected" or "Cancelled" or "Out of stock" => "badge--danger",
        _ => "badge--neutral"
    };

    public static string StockClass(int avail) =>
        avail <= 0 ? "badge--danger" : (avail <= 6 ? "badge--warn" : "badge--ok");

    public static string SourceClass(string source) =>
        source == "Distributor" ? "src--dist" : "src--own";

    public static string CatClass(string cat) => cat switch
    {
        "Servers" => "tile--servers",
        "Networking" => "tile--networking",
        "Laptops" => "tile--laptops",
        "Components" => "tile--components",
        "Security" => "tile--security",
        "Power" => "tile--power",
        _ => "tile--peripherals"
    };

    public static string Initials(string name)
    {
        var parts = (name ?? "?").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        var s = parts[0].Substring(0, 1);
        if (parts.Length > 1) s += parts[1].Substring(0, 1);
        return s.ToUpperInvariant();
    }
}
