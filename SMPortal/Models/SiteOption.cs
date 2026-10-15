namespace SMPortal.Models;

/// <summary>
/// A store the signed-in admin may act for, as returned by GET /api/Site.
/// </summary>
/// <remarks>
/// Identity and display only. The full site row carries commercial configuration — margin
/// floor, price visibility, ordering mode — that the selector has no business knowing, and
/// that changing from a dropdown would be a far larger action than switching which store you
/// are looking at.
///
/// IsActive arrived with store creation (T9): an admin of every store also sees, and sets up,
/// the stores that are not open yet.
///
/// PricesIncludeTax and StandardTaxRatePct are display values, as the currency is (T10): a
/// margin sets a net cost against a price, and on a store whose prices include tax the price
/// has to lose its tax first. See <c>IAdminDataService.NetOfTax</c>.
/// </remarks>
public sealed record SiteOption(int Id, string SiteKey, string Name, string? Country, string? CurrencyCode,
    bool IsActive = true, bool PricesIncludeTax = false, decimal StandardTaxRatePct = 0);
