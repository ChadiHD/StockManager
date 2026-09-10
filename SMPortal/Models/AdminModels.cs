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
}

public class QuoteLine
{
    /// <summary>Database id of the QuoteLine row, needed to address it for removal.</summary>
    public int LineId { get; set; }

    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public int Qty { get; set; }
    public decimal List { get; set; }
    public int Disc { get; set; }
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
