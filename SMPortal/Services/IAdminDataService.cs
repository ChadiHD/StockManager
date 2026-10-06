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

    /// <summary>
    /// False for an admin who has been given no store yet: the snapshot is then empty, and the
    /// layout says why rather than reporting the API unreachable.
    /// </summary>
    bool HasStoreAccess { get; }

    /// <summary>
    /// Whether this admin may act for every store, which managing staff takes (T9). Pages and
    /// navigation for staff are hidden from anybody else; the API refuses them regardless.
    /// </summary>
    bool ManagesAllStores { get; }

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

    /// <summary>
    /// Creates a staff login with the password the admin chose, the given role, and every store
    /// or the listed ones. Returns why it was refused — the password rules, usually — or null
    /// once it exists.
    /// </summary>
    Task<string?> AddUser(string name, string email, string role, string password,
        bool allSites, IEnumerable<int> siteIds);

    /// <summary>
    /// Sets a member of staff's roles and stores, by their id. Returns why it was refused — the
    /// API will not leave the deployment without an admin of every store — or null.
    /// </summary>
    Task<string?> UpdateUserAccess(string userId, IEnumerable<string> roles,
        bool allSites, IEnumerable<int> siteIds);

    /// <summary>
    /// The acting store's settings, fetched when the settings screen opens rather than held in
    /// the snapshot: one store's configuration, read by one page (T9).
    /// </summary>
    Task<StoreSettingsView?> GetStoreSettings();

    /// <summary>Saves the acting store's settings. Returns why they were refused, or null.</summary>
    Task<string?> SaveStoreSettings(StoreSettings settings);

    /// <summary>The acting store's content pages, fetched when the content screen opens (T9).</summary>
    Task<IReadOnlyList<ContentPageItem>> GetContentPages();

    /// <summary>
    /// Saves one content page. Returns the page as stored — its body cleaned to the allow-list —
    /// or why it was refused.
    /// </summary>
    Task<(ContentPageItem? Saved, string? Refusal)> SaveContentPage(string key, string title, string? lede, string? bodyHtml);

    /// <summary>
    /// Creates a store, closed, to be configured and then opened (T9). Admins of every store
    /// only. Returns the new store, or why it was refused.
    /// </summary>
    Task<(SiteOption? Created, string? Refusal)> CreateStore(NewStore store);

    /// <summary>
    /// Opens a store to customers — refused, naming what is missing, until its checklist is met —
    /// or closes it. Returns why it was refused, or null.
    /// </summary>
    Task<string?> SetStoreOpen(string siteKey, bool open);

    /// <summary>Every message the acting store sends, with its wording (T9).</summary>
    Task<IReadOnlyList<EmailWording>> GetEmailWording();

    /// <summary>
    /// Sets the store's own wording for one message; both halves blank goes back to the
    /// platform's. Returns the message as stored, or why the wording was refused.
    /// </summary>
    Task<(EmailWording? Saved, string? Refusal)> SaveEmailWording(string key, string? subject, string? body);

    /// <summary>The signed-in user's own password. Returns why it was refused, or null.</summary>
    Task<string?> ChangePassword(string currentPassword, string newPassword);

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

    /// <summary>
    /// The acting store's choice about one product: Show, Hide or null to follow the category
    /// mapping, the category a shown product is filed under, and its featured flag and badge.
    /// Returns why it was refused, or null.
    /// </summary>
    Task<string?> SetPlacement(string sku, string? visibility, int? storeCategoryId, bool featured, string? badge);

    /// <summary>The same visibility for a selection of products, applied whole. Refusal or null.</summary>
    Task<string?> SetVisibility(IReadOnlyCollection<string> skus, string? visibility);

    /// <summary>
    /// The feed categories and where the acting store files each, with the store's own
    /// categories to choose from.
    /// </summary>
    /// <remarks>
    /// Fetched on opening the screen rather than held in the snapshot, like contacts and
    /// documents: it is one store's view of a list most screens never need.
    /// </remarks>
    Task<CategoryMappingView> GetCategoryMappings();

    /// <summary>Files a feed category under a store category, or stops selling it when null.</summary>
    Task<string?> MapCategory(string feedValue, int? storeCategoryId);

    /// <summary>
    /// Creates (<c>Id</c> 0) or edits one of the acting store's categories. Returns why it was
    /// refused, or null (T9).
    /// </summary>
    Task<string?> SaveStoreCategory(StoreCategoryOption category);
}
