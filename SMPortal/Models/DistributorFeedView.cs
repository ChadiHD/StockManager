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

    /// <summary>
    /// Set while a sync of this feed is running, so the page can say so rather than offering a
    /// button that will be refused.
    /// </summary>
    public DateTime? SyncStartedUtc { get; set; }

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

    /// <summary>
    /// Nothing ran because a sync of this feed was already in progress — the nightly schedule,
    /// or another operator. Not a failure, and it must not be reported as one.
    /// </summary>
    public bool AlreadyRunning { get; set; }

    public string? Error { get; set; }

    /// <summary>
    /// Field names the tested feed actually carries. Populated by a test, not by a sync.
    /// </summary>
    public List<FeedFieldSampleView> DiscoveredFields { get; set; } = new();
}

/// <summary>One recorded attempt to sync a feed, as the admin UI reads it.</summary>
/// <remarks>
/// Fetched per feed rather than held in the snapshot, like contacts and documents: an operator
/// opens one feed's history at a time, and pulling every attempt for every feed into memory on
/// every page load would be the wrong trade.
/// </remarks>
public class FeedSyncLogView
{
    public int Id { get; set; }
    public int FeedId { get; set; }
    public string? FeedName { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime FinishedUtc { get; set; }
    public bool Succeeded { get; set; }
    public int RecordCount { get; set; }
    public int Imported { get; set; }
    public int Delisted { get; set; }
    public string? Message { get; set; }

    /// <summary>"Schedule" or "Operator".</summary>
    public string? TriggeredBy { get; set; }

    public TimeSpan Duration => FinishedUtc - StartedUtc;
}

/// <summary>A feed that has not delivered inside this store's staleness threshold.</summary>
public class StaleFeedView
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime? LastSyncedUtc { get; set; }
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
