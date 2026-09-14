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

    // Identity columns, used to resolve product images from Icecat.
    public string? FieldManufacturer { get; set; } = "Manufacturer";
    public string? FieldMpn { get; set; } = "ManufacturerPartNumber";
    public string? FieldEan { get; set; } = "EAN";
    public string? FieldIcecat { get; set; } = "IceCatID";

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

    /// <summary>
    /// Field names the tested feed actually carries. Populated by a test, not by a sync.
    /// </summary>
    public List<FeedFieldSampleView> DiscoveredFields { get; set; } = new();
}

/// <summary>
/// One field found in a feed. Shown after a test so the mapping below can be filled in from
/// what the feed contains rather than from guesswork — a name that matches nothing imports as
/// NULL on every row while the sync still reports success.
/// </summary>
public class FeedFieldSampleView
{
    public string? Name { get; set; }
    public int PopulatedPct { get; set; }
    public string? SampleValue { get; set; }
}
