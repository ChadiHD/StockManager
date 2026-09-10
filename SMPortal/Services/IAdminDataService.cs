using SMPortal.Models;

namespace SMPortal.Services;

// Single seam the UI talks to, now backed by the StockManager API.
//
// Reads stay synchronous on purpose: the service holds a snapshot that EnsureLoadedAsync fills
// from the API, and AdminLayout waits for that before rendering a page. That keeps every list
// and detail view in the admin area unchanged. Mutations are Task-returning because a Blazor
// WebAssembly client cannot block on an HTTP call.
public interface IAdminDataService
{
    IReadOnlyList<Account> Accounts { get; }
    IReadOnlyList<Quote> Quotes { get; }
    IReadOnlyList<Order> Orders { get; }
    IReadOnlyList<Product> Products { get; }
    IReadOnlyList<Group> Groups { get; }
    IReadOnlyList<User> Users { get; }
    IReadOnlyList<ActivityItem> Activity { get; }
    IReadOnlyList<ReportRow> Reports { get; }

    /// <summary>Loads the snapshot once per session. Safe to call repeatedly.</summary>
    Task EnsureLoadedAsync();

    /// <summary>Re-reads everything from the API.</summary>
    Task RefreshAsync();

    Account? GetAccount(string id);
    Quote? GetQuote(string id);
    Order? GetOrder(string id);
    Product? GetProduct(string sku);
    Group? GetGroup(string name);
    User? GetUser(string email);

    int DiscountFor(string groupName);

    // Mutations (return the affected entity where a new id is generated)
    Task ApproveAccount(string id);
    Task RejectAccount(string id);
    Task ToggleSuspend(string id);
    Task UpdateTerms(string id, string group, string payment, string terms, decimal credit);

    Task MarkOrderFulfilled(string id);
    Task<Order?> ConvertQuoteToOrder(string quoteId);
    Task<Quote?> AddQuote(string accountName, string currency);
    Task<Order?> AddOrder(string accountName, string currency);
    /// <summary>Adds a line for a chosen product. Returns false when nothing was added.</summary>
    Task<bool> AddQuoteLine(string quoteId, string sku, int quantity, int discountPct);

    /// <summary>Removes one line from a quote. Returns false when nothing was removed.</summary>
    Task<bool> DeleteQuoteLine(string quoteId, int lineId);

    Task<Account?> AddAccount(Account draft);
    Task<Product?> AddProduct(Product draft);
    Task UpdateProduct(Product edited);
    Task<Group?> AddGroup(Group draft);
    Task UpdateGroup(string name, int discount, string terms, string note);

    Task<User?> AddUser(string name, string email, string role);
    Task UpdateUserRoles(string email, IEnumerable<string> roles);

    Task SyncFeeds();

    // ---- Distributor feed management -------------------------------------------------------
    IReadOnlyList<DistributorFeedView> Feeds { get; }

    Task<DistributorFeedView?> SaveFeed(DistributorFeedView feed);
    Task DeleteFeed(int id);

    /// <summary>Connects and parses without importing, so a feed can be verified before saving.</summary>
    Task<FeedSyncOutcome> TestFeed(DistributorFeedView feed);

    Task<FeedSyncOutcome> SyncFeed(int id);

    /// <summary>
    /// Runs one batch of Icecat image lookups for products with no image, and returns a
    /// human-readable summary. Returns the disabled message when no Icecat account is set up.
    /// </summary>
    Task<string> FetchProductImages(int take);
}
