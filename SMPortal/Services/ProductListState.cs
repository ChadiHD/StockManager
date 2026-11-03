namespace SMPortal.Services;

/// <summary>
/// The products list's search, filter and page, held for the life of the app rather than the page,
/// so that opening a product and coming back finds the list as it was left. The page's own fields
/// went with the page, and every visit to a product cost the admin their search.
/// </summary>
/// <remarks>Signing out reloads the portal, which starts this again.</remarks>
public sealed class ProductListState
{
    public string Search { get; set; } = "";
    public string Filter { get; set; } = "All";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}
