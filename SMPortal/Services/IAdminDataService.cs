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
    /// <summary>
    /// The stores this admin may act for, and the one the snapshot below belongs to.
    /// </summary>
    /// <remarks>
    /// Everything else on this interface is implicitly scoped to <see cref="CurrentSiteKey"/>:
    /// the service puts it in a header on the shared HttpClient, so no call passes a site and
    /// none can forget to. Switching stores therefore invalidates the whole snapshot, which is
    /// why <see cref="SwitchSiteAsync"/> reloads rather than refreshing a part of it.
    /// </remarks>
    IReadOnlyList<SiteOption> Sites { get; }

    string? CurrentSiteKey { get; }

    /// <summary>Changes the store this workspace is showing, and reloads everything.</summary>
    Task SwitchSiteAsync(string siteKey);

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

    /// <summary>
    /// Approves an application on the given pricing group. Returns what went wrong, or null.
    /// </summary>
    /// <remarks>
    /// Text rather than a throw because the interesting failure is a conflict: somebody else
    /// decided this application while the screen was open, and that is something to tell the
    /// user rather than an exception to surface.
    /// </remarks>
    Task<string?> ApproveAccount(string id, string? group);

    /// <summary>Turns an application down. The reason is quoted to the applicant verbatim.</summary>
    Task<string?> RejectAccount(string id, string reason);

    Task ToggleSuspend(string id);

    /// <summary>The people on one account, primary contact first.</summary>
    Task<IReadOnlyList<Contact>> GetContacts(string id);

    Task<IReadOnlyList<AccountAddress>> GetAddresses(string id);

    /// <summary>
    /// The paperwork on one account. Asynchronous, unlike the other reads here, because it
    /// is fetched per account rather than held in the snapshot.
    /// </summary>
    Task<IReadOnlyList<AccountDocument>> GetDocuments(string id);

    /// <summary>The bytes of one document, base64-encoded. Null when the API refuses it.</summary>
    Task<(string Name, string ContentType, string Base64)?> GetDocumentContent(int documentId);

    Task<bool> SetDocumentStatus(int documentId, string status);
    Task UpdateTerms(string id, string group, string payment, string terms, decimal credit);

    Task MarkOrderFulfilled(string id);
    /// <summary>Converts a quote, or reports that somebody else already decided it.</summary>
    Task<QuoteConversion> ConvertQuoteToOrder(string quoteId);
    Task<Quote?> AddQuote(string accountName, string currency);
    Task<Order?> AddOrder(string accountName, string currency);
    /// <summary>Adds a line for a chosen product. Returns false when nothing was added.</summary>
    Task<bool> AddQuoteLine(string quoteId, string sku, int quantity, int discountPct);

    /// <summary>Removes one line from a quote. Returns false when nothing was removed.</summary>
    Task<bool> DeleteQuoteLine(string quoteId, int lineId);

    /// <summary>Re-prices one line. False when nothing was written — including an accepted quote.</summary>
    Task<bool> UpdateQuoteLine(string quoteId, int lineId, int quantity, decimal discountPct);

    /// <summary>Sends a priced quote to the customer, making it decidable.</summary>
    Task<QuotePricing> SendQuoteToCustomer(string quoteId);

    Task<Account?> AddAccount(Account draft);
    Task<Product?> AddProduct(Product draft);
    Task UpdateProduct(Product edited);
    Task<Group?> AddGroup(Group draft);
    Task UpdateGroup(string name, int discount, string terms, string note);

    Task<User?> AddUser(string name, string email, string role);
    Task UpdateUserRoles(string email, IEnumerable<string> roles);

    Task SyncFeeds();

    /// <summary>
    /// Every recorded attempt against one feed, newest first.
    /// </summary>
    /// <remarks>
    /// Asynchronous and per feed, like <see cref="GetDocuments"/> and for the same reason: an
    /// operator opens one feed's history at a time, and holding every attempt for every feed in
    /// the snapshot would be the wrong trade. A page calling this needs
    /// <c>OnParametersSetAsync</c>, not <c>OnParametersSet</c>.
    /// </remarks>
    Task<IReadOnlyList<FeedSyncLogView>> GetFeedHistory(int feedId);

    /// <summary>Recent attempts across every feed, newest first.</summary>
    Task<IReadOnlyList<FeedSyncLogView>> GetRecentFeedHistory();

    /// <summary>
    /// Feeds this store has not heard from inside its staleness threshold. Empty when the
    /// store has set no threshold.
    /// </summary>
    Task<IReadOnlyList<StaleFeedView>> GetStaleFeeds();

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
