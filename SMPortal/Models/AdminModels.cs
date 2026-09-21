using System.Linq;

namespace SMPortal.Models;

// Plain mutable models matching the prototype data shapes. Money is decimal.
// These are deliberately API-friendly: an HTTP-backed IAdminDataService can
// deserialize the same shapes without any change to the UI components.

public class Account
{
    public string Id { get; set; } = "";
    public string Company { get; set; } = "";
    public string Contact { get; set; } = "";
    public string Email { get; set; } = "";
    public string Country { get; set; } = "";
    public string Currency { get; set; } = "EUR";
    public string Group { get; set; } = "Reseller";
    public string Payment { get; set; } = "Card";
    public string Terms { get; set; } = "Prepaid";
    public decimal Credit { get; set; }
    public string Status { get; set; } = "Pending";
    public string Since { get; set; } = "";

    /// <summary>Company identifiers collected at registration; either may be absent.</summary>
    public string Vat { get; set; } = "";

    public string Registration { get; set; } = "";

    /// <summary>
    /// Who decided this application and when, from spAccount_Approve / spAccount_Reject.
    /// Empty until somebody has decided — an account created by an admin has no applicant
    /// and no decision.
    /// </summary>
    public string DecidedBy { get; set; } = "";

    public string DecidedOn { get; set; } = "";

    /// <summary>What the applicant was told. Present only on a rejection.</summary>
    public string RejectionReason { get; set; } = "";
}

/// <summary>
/// A file an applicant uploaded, as the admin API describes it.
/// </summary>
/// <remarks>
/// No stored name. That is the key into the document store, the API does not project it, and
/// a client that held one would be a client that could be talked into leaking it.
/// </remarks>
public class AccountDocument
{
    public int Id { get; set; }
    public string Kind { get; set; } = "";

    /// <summary>What the customer called it. Untrusted text — render it, never use it as a path.</summary>
    public string Name { get; set; } = "";

    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Uploaded { get; set; } = "";
    public string Status { get; set; } = "Pending";
}

public class QuoteLine
{
    /// <summary>Database id of the QuoteLine row, needed to address it for removal.</summary>
    public int LineId { get; set; }

    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public int Qty { get; set; }
    public decimal List { get; set; }
    /// <summary>Percentage off list, to two places: a floored price has a fractional one.</summary>
    public decimal Disc { get; set; }
    public decimal Net { get; set; }
    public decimal LineTotal => Qty * Net;
}

public class Quote
{
    public string Id { get; set; } = "";
    public string Account { get; set; } = "";
    public string Currency { get; set; } = "EUR";
    public decimal Value { get; set; }
    public string Status { get; set; } = "Requested";
    public string Created { get; set; } = "";
    public string Expires { get; set; } = "";
    public int Lines { get; set; }
    public List<QuoteLine> LineItems { get; set; } = new();
}

public class OrderLine
{
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public int Qty { get; set; }
    public decimal Price { get; set; }
    public decimal LineTotal => Qty * Price;
}

public class Order
{
    public string Id { get; set; } = "";
    public string Account { get; set; } = "";
    public string Currency { get; set; } = "EUR";
    public decimal Value { get; set; }
    public string Status { get; set; } = "Awaiting payment";
    public string Placed { get; set; } = "";
    public int Items { get; set; }
    public string From { get; set; } = "\u2014";
    public List<OrderLine> LineItems { get; set; } = new();
}

public class Product
{
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string Cat { get; set; } = "Components";
    public string Source { get; set; } = "Own";
    public string Dist { get; set; } = "\u2014";
    public string DistSku { get; set; } = "\u2014";
    public decimal Cost { get; set; }
    public decimal Price { get; set; }
    public int Avail { get; set; }
    public string Synced { get; set; } = "\u2014";
    public bool Stale { get; set; }
    public string Image { get; set; } = "";

    // The feed splits a part across two columns: Name carries the spec string
    // ("E14 G2 i7-1165G7/16GB/..."), Desc the brand and model ("LENOVO ThinkPad E14 G2").
    // Searching only Name therefore finds nothing by brand, which is how buyers search.
    public string Desc { get; set; } = "";

    // Carried through from the distributor feed so operators can search on the manufacturer's
    // own identifiers, which is how the trade actually refers to a part.
    public string Manufacturer { get; set; } = "";
    public string Mpn { get; set; } = "";
    public string Ean { get; set; } = "";

    public bool IsDistributor => Source == "Distributor";
    public int MarginPct => Price > 0 ? (int)Math.Round((1 - Cost / Price) * 100) : 0;
    public string StockKind => Avail <= 0 ? "Out of stock" : (Avail <= 6 ? "Low" : "In stock");
}

public class Group
{
    public string Name { get; set; } = "";
    public int Discount { get; set; }
    public int Accounts { get; set; }
    public int Overrides { get; set; }
    public string Terms { get; set; } = "Net 30";
    public string Note { get; set; } = "";
}

public class User
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Roles { get; set; } = "Staff";
    public IEnumerable<string> RoleList =>
        Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public class ActivityItem
{
    public string When { get; set; } = "";
    public string Who { get; set; } = "";
    public string What { get; set; } = "";
    public string Type { get; set; } = "";
    public string Status { get; set; } = "";
    public string Screen { get; set; } = "dashboard";
}

public class ReportRow
{
    public string Date { get; set; } = "";
    public string Account { get; set; } = "";
    public string Ref { get; set; } = "";
    public string Cur { get; set; } = "EUR";
    public decimal Net { get; set; }
    public decimal Vat { get; set; }
    public decimal Total { get; set; }
}

// Support shapes for the account detail view
public class Contact
{
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";

    /// <summary>The contact the account was registered by, shown first.</summary>
    public bool IsPrimary { get; set; }

    /// <summary>"Active" | "Invited" | "Disabled", independent of the account's status.</summary>
    public string Status { get; set; } = "Active";
}

/// <summary>A billing or delivery address on an account.</summary>
public class AccountAddress
{
    public int Id { get; set; }
    public string Kind { get; set; } = "Billing";
    public string Line1 { get; set; } = "";
    public string Line2 { get; set; } = "";
    public string City { get; set; } = "";
    public string Region { get; set; } = "";
    public string PostCode { get; set; } = "";
    public string Country { get; set; } = "";
    public bool IsDefault { get; set; }

    /// <summary>One line, skipping the parts this address does not have.</summary>
    public string OneLine => string.Join(", ", new[] { Line1, Line2, City, Region, PostCode, Country }
        .Where(part => !string.IsNullOrWhiteSpace(part)));
}

public class DocItem
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Status { get; set; } = "";
}

public class HistoryItem
{
    public string When { get; set; } = "";
    public string Text { get; set; } = "";
    public string Who { get; set; } = "";
}

/// <summary>
/// What came of converting a quote to an order.
/// </summary>
/// <remarks>
/// Three outcomes rather than two, shaped like <see cref="FeedSyncOutcome"/> and for the same
/// reason: a quote somebody else already decided is not a fault in the request, and wording it
/// as a failure teaches an operator to discount the message when a conversion really has
/// broken. <c>spOrder_ConvertFromQuote</c> claims the quote's status transition, so a refused
/// claim means a customer accepted it — or another admin converted it — while this screen was
/// open.
/// </remarks>
/// <summary>
/// What "Send to customer" did.
/// </summary>
/// <remarks>
/// Three outcomes for the reason <see cref="QuoteConversion"/> has three. Pricing claims the
/// quote's status, so a refused claim means the customer accepted or rejected it while this
/// screen was open — ordinary, not a fault. An operator told "that failed" about it learns to
/// discount the message that matters.
/// </remarks>
public enum QuotePricing
{
    Sent,
    AlreadyDecided,
    Failed
}

public sealed class QuoteConversion
{
    private QuoteConversion(Order? order, bool alreadyDecided)
    {
        Order = order;
        AlreadyDecided = alreadyDecided;
    }

    public Order? Order { get; }

    /// <summary>The quote had already been accepted or rejected. Nothing was created.</summary>
    public bool AlreadyDecided { get; }

    public bool Succeeded => Order is not null;

    public static QuoteConversion Created(Order? order) => new(order, false);

    public static QuoteConversion Conflict() => new(null, true);

    public static QuoteConversion Failed() => new(null, false);
}
