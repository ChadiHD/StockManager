namespace SMPortal.Models;

/// <summary>
/// One store's configuration as the settings screen edits it (T9): the dbo.Site row, less
/// nothing. Sent back whole, because spSite_Update writes every column it is given.
/// </summary>
public sealed class StoreSettings
{
    public int Id { get; set; }
    public string SiteKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string Domain { get; set; } = "";
    public string Country { get; set; } = "";
    public string CurrencyCode { get; set; } = "";
    public string Locale { get; set; } = "";
    public string OrderMode { get; set; } = "";
    public string RegistrationFieldSet { get; set; } = "";
    public string PriceDisplay { get; set; } = "";
    public decimal MinMarginPct { get; set; }
    public int FeedStaleAfterHours { get; set; }
    public bool HideStaleProducts { get; set; }
    public string? OperatorEmail { get; set; }
    public string TaxRuleSet { get; set; } = "";
    public decimal StandardTaxRatePct { get; set; }
    public string? TaxRegistrationNumber { get; set; }
    public string? MailFromAddress { get; set; }
    public string? LegalName { get; set; }
    public string? CompanyRegistrationNumber { get; set; }
    public string? RegisteredAddress { get; set; }
    public bool IsActive { get; set; }

    /// <summary>The store already holds prices in its currency, so the code cannot change.</summary>
    public bool CurrencyLocked { get; set; }
}

/// <summary>The settings, and what each keyed one may be — the API's lists, not the portal's.</summary>
public sealed class StoreSettingsView
{
    public StoreSettings Site { get; set; } = new();

    /// <summary>Only an admin of every store may move a store to another domain.</summary>
    public bool CanChangeDomain { get; set; }

    public List<string> OrderModes { get; set; } = new();
    public List<string> RegistrationFieldSets { get; set; } = new();
    public List<string> TaxRuleSets { get; set; } = new();
    public List<string> PriceDisplays { get; set; } = new();
}
