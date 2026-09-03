namespace SMPortal.Shared.Admin;

/// <summary>
/// One row in a <see cref="Combo"/>. Kept deliberately flat so any list can be projected onto
/// it without the component needing to know what it is picking.
/// </summary>
/// <param name="Value">What gets bound when the row is chosen.</param>
/// <param name="Label">The primary line, and what the closed control displays.</param>
/// <param name="Sub">A secondary line — a product name under a SKU, a contact under a company.</param>
/// <param name="Meta">A short trailing note, right-aligned. Stock counts live here.</param>
/// <param name="Disabled">Shown but not selectable, so an operator can see why it is unavailable.</param>
public sealed record ComboOption(
    string Value,
    string Label,
    string? Sub = null,
    string? Meta = null,
    bool Disabled = false);
