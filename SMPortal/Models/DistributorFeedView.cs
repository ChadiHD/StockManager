namespace SMPortal.Models;

// A distributor feed as the admin UI sees it. There is deliberately no Password property on
// the read model — the API never returns one. HasCredential is all the UI needs to know.
public class DistributorFeedView
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "";
    public string RemoteDirectory { get; set; } = ".";
    public string? HostKeySha256 { get; set; }
    public bool Enabled { get; set; } = true;
    public bool HasCredential { get; set; }
    public string? SecretProvider { get; set; }
    public DateTime? LastSyncedUtc { get; set; }
    public string? LastSyncStatus { get; set; }

    public string FieldSku { get; set; } = "FlexITPartNumber";
    public string FieldName { get; set; } = "Description";
    public string? FieldDescription { get; set; } = "WebDescription";
    public string? FieldCategory { get; set; } = "Category";
    public string? FieldCost { get; set; } = "SalesPrice";
    public string? FieldSrp { get; set; } = "SRP";
    public string? FieldQuantity { get; set; } = "StockQuantity";

    /// <summary>Only ever sent to the server; blank on edit means "keep the stored password".</summary>
    public string? Password { get; set; }
}

public class FeedSyncOutcome
{
    public string? Distributor { get; set; }
    public int RecordCount { get; set; }
    public int Imported { get; set; }
    public int Delisted { get; set; }
    public bool Succeeded { get; set; }
    public string? Error { get; set; }
}
