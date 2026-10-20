using SMDataManager.Library.Models;

namespace SMStore.Catalog;

/// <summary>
/// What a product page shows (T11): the product, and when it is a style, the variants of it
/// this viewer may buy.
/// </summary>
/// <param name="Product">The style when the SKU named a style or one of its variants.</param>
/// <param name="Variants">Empty for a product with no variants. In the order they were added.</param>
/// <param name="SelectedSku">The variant the URL named, so a link to "M / Navy" opens on it.</param>
/// <param name="ChoiceLabel">The style's option names, "Size / Colour", in the store's words.</param>
public sealed record ProductPage(
    CatalogItemModel Product,
    IReadOnlyList<VariantChoice> Variants,
    string? SelectedSku,
    string? ChoiceLabel);

/// <summary>One variant as a customer chooses it: "M / Navy", and whether it is in stock.</summary>
public sealed record VariantChoice(string Sku, string Label, bool InStock);
