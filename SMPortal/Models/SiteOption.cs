namespace SMPortal.Models;

/// <summary>
/// A store the signed-in admin may act for, as returned by GET /api/Site.
/// </summary>
/// <remarks>
/// Identity and display only. The full site row carries commercial configuration — margin
/// floor, price visibility, ordering mode — that the selector has no business knowing, and
/// that changing from a dropdown would be a far larger action than switching which store you
/// are looking at.
/// </remarks>
public sealed record SiteOption(int Id, string SiteKey, string Name, string? Country, string? CurrencyCode);
