using System.Net.Http.Json;
using SMPortal.Models;

namespace SMPortal.Services;

// Talks to the StockManager API and projects the responses onto the view models the admin
// pages already bind to. Uses the same HttpClient instance that AuthStateProvider puts the
// bearer token on, and resolves the API base address from wwwroot/appsettings.json.
public class AdminDataService : IAdminDataService
{
    private readonly HttpClient _client;
    private readonly string _api;

    private readonly List<Account> _accounts = new();
    private readonly List<Quote> _quotes = new();
    private readonly List<Order> _orders = new();
    private readonly List<Product> _products = new();
    private readonly List<Group> _groups = new();
    private readonly List<User> _users = new();
    private readonly List<ActivityItem> _activity = new();
    private readonly List<ReportRow> _reports = new();
    private readonly List<DistributorFeedView> _feeds = new();

    // Reference ("AC-2041") -> database id, so mutations can address the int-keyed endpoints
    // without the UI having to know about numeric ids.
    private readonly Dictionary<string, int> _accountIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _groupSlugs = new(StringComparer.OrdinalIgnoreCase);

    private bool _loaded;

    public AdminDataService(HttpClient client, IConfiguration config)
    {
        _client = client;
        _api = (config["api"] ?? throw new InvalidOperationException("The api configuration value is required."))
            .TrimEnd('/');
    }

    public IReadOnlyList<Account> Accounts => _accounts;
    public IReadOnlyList<Quote> Quotes => _quotes;
    public IReadOnlyList<Order> Orders => _orders;
    public IReadOnlyList<Product> Products => _products;
    public IReadOnlyList<Group> Groups => _groups;
    public IReadOnlyList<User> Users => _users;
    public IReadOnlyList<ActivityItem> Activity => _activity;
    public IReadOnlyList<ReportRow> Reports => _reports;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        var accountsTask = GetListAsync<AccountDto>("api/Account");
        var groupsTask = GetListAsync<GroupDto>("api/CustomerGroup");
        var quotesTask = GetListAsync<QuoteDto>("api/Quote");
        var ordersTask = GetListAsync<OrderDto>("api/Order");
        var productsTask = GetListAsync<ProductDto>("api/Product/Catalog");
        var reportsTask = GetListAsync<ReportDto>("api/Order/Report");
        var activityTask = GetListAsync<ActivityDto>("api/Order/Activity?take=10");
        var usersTask = GetListAsync<AppUserDto>("api/User/Admin/GetAllUsers");
        var staffTask = GetListAsync<StaffDto>("api/User/Admin/Staff");
        var feedsTask = GetListAsync<DistributorFeedView>("api/DistributorFeed");

        await Task.WhenAll(accountsTask, groupsTask, quotesTask, ordersTask,
            productsTask, reportsTask, activityTask, usersTask, staffTask, feedsTask);

        Replace(_feeds, await feedsTask);

        var accounts = await accountsTask;
        var groups = await groupsTask;
        var quotes = await quotesTask;
        var orders = await ordersTask;
        var products = await productsTask;
        var staff = await staffTask;

        _accountIds.Clear();
        foreach (var account in accounts)
        {
            _accountIds[account.Reference ?? account.Id.ToString()] = account.Id;
        }

        _groupSlugs.Clear();
        foreach (var group in groups)
        {
            _groupSlugs[group.Name ?? string.Empty] = group.Slug ?? string.Empty;
        }

        Replace(_accounts, accounts.Select(MapAccount));
        Replace(_groups, groups.Select(MapGroup));
        Replace(_products, products.Select(MapProduct));
        Replace(_reports, (await reportsTask).Select(MapReport));
        Replace(_activity, (await activityTask).Select(MapActivity));
        Replace(_users, MapUsers(await usersTask, staff));

        // Line items are fetched per document, in parallel, so the detail pages already have
        // their lines when the user opens one.
        var quoteLineTasks = quotes
            .Where(quote => !string.IsNullOrWhiteSpace(quote.Reference))
            .ToDictionary(
                quote => quote.Reference!,
                quote => GetListAsync<QuoteLineDto>($"api/Quote/{Uri.EscapeDataString(quote.Reference!)}/Lines"));
        var orderLineTasks = orders
            .Where(order => !string.IsNullOrWhiteSpace(order.Reference))
            .ToDictionary(
                order => order.Reference!,
                order => GetListAsync<OrderLineDto>($"api/Order/{Uri.EscapeDataString(order.Reference!)}/Lines"));

        await Task.WhenAll(quoteLineTasks.Values.Concat<Task>(orderLineTasks.Values));

        Replace(_quotes, quotes.Select(quote => MapQuote(quote,
            quoteLineTasks.TryGetValue(quote.Reference ?? string.Empty, out var quoteLines)
                ? quoteLines.Result
                : new List<QuoteLineDto>())));
        Replace(_orders, orders.Select(order => MapOrder(order,
            orderLineTasks.TryGetValue(order.Reference ?? string.Empty, out var orderLines)
                ? orderLines.Result
                : new List<OrderLineDto>())));

        _loaded = true;
    }

    // ---- Lookups ------------------------------------------------------------------------

    public Account? GetAccount(string id) =>
        _accounts.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

    public Quote? GetQuote(string id) =>
        _quotes.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

    public Order? GetOrder(string id) =>
        _orders.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

    public Product? GetProduct(string sku) =>
        _products.FirstOrDefault(x => string.Equals(x.Sku, sku, StringComparison.OrdinalIgnoreCase));

    public Group? GetGroup(string name) =>
        _groups.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

    public User? GetUser(string email) =>
        _users.FirstOrDefault(x => string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase));

    public int DiscountFor(string groupName) => GetGroup(groupName)?.Discount ?? 0;

    // ---- Account mutations --------------------------------------------------------------

    public Task ApproveAccount(string id) => SetAccountStatus(id, "Approved");

    public Task RejectAccount(string id) => SetAccountStatus(id, "Rejected");

    public Task ToggleSuspend(string id)
    {
        var account = GetAccount(id);
        var next = string.Equals(account?.Status, "Suspended", StringComparison.OrdinalIgnoreCase)
            ? "Approved"
            : "Suspended";

        return SetAccountStatus(id, next);
    }

    private async Task SetAccountStatus(string reference, string status)
    {
        if (!_accountIds.TryGetValue(reference, out int accountId)) return;

        var response = await _client.PutAsJsonAsync($"{_api}/api/Account/{accountId}/Status", new { Status = status });
        response.EnsureSuccessStatusCode();

        await RefreshAsync();
    }

    public async Task UpdateTerms(string id, string group, string payment, string terms, decimal credit)
    {
        if (!_accountIds.TryGetValue(id, out int accountId)) return;

        var response = await _client.PutAsJsonAsync($"{_api}/api/Account/{accountId}/Terms", new
        {
            CustomerGroupId = await ResolveGroupId(group),
            PaymentMethod = payment,
            PaymentTerms = terms,
            CreditLimit = credit
        });
        response.EnsureSuccessStatusCode();

        await RefreshAsync();
    }

    public async Task<Account?> AddAccount(Account draft)
    {
        var response = await _client.PostAsJsonAsync($"{_api}/api/Account", new
        {
            Company = draft.Company,
            ContactName = draft.Contact,
            Email = draft.Email,
            Country = draft.Country,
            Currency = draft.Currency,
            CustomerGroupId = await ResolveGroupId(draft.Group),
            PaymentMethod = draft.Payment,
            PaymentTerms = draft.Terms,
            CreditLimit = draft.Credit,
            Status = string.IsNullOrWhiteSpace(draft.Status) ? "Pending" : draft.Status
        });
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<AccountDto>();
        await RefreshAsync();

        return created is null ? null : GetAccount(created.Reference ?? string.Empty);
    }

    private async Task<int?> ResolveGroupId(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return null;

        var groups = await GetListAsync<GroupDto>("api/CustomerGroup");

        return groups.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, groupName, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    // ---- Quote / order mutations ---------------------------------------------------------

    public async Task MarkOrderFulfilled(string id)
    {
        var response = await _client.PutAsJsonAsync(
            $"{_api}/api/Order/{Uri.EscapeDataString(id)}/Status", new { Status = "Fulfilled" });
        response.EnsureSuccessStatusCode();

        await RefreshAsync();
    }

    public async Task<Order?> ConvertQuoteToOrder(string quoteId)
    {
        var response = await _client.PostAsJsonAsync($"{_api}/api/Order/FromQuote",
            new { QuoteReference = quoteId });
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<OrderDto>();
        await RefreshAsync();

        return created is null ? null : GetOrder(created.Reference ?? string.Empty);
    }

    public async Task<Quote?> AddQuote(string accountName, string currency)
    {
        int? accountId = ResolveAccountIdByCompany(accountName);
        if (accountId is null) return null;

        var response = await _client.PostAsJsonAsync($"{_api}/api/Quote", new
        {
            AccountId = accountId.Value,
            Currency = currency,
            ExpiresDate = DateTime.UtcNow.AddDays(14)
        });
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<QuoteDto>();
        await RefreshAsync();

        return created is null ? null : GetQuote(created.Reference ?? string.Empty);
    }

    public async Task<Order?> AddOrder(string accountName, string currency)
    {
        int? accountId = ResolveAccountIdByCompany(accountName);
        if (accountId is null) return null;

        var response = await _client.PostAsJsonAsync($"{_api}/api/Order", new
        {
            AccountId = accountId.Value,
            Currency = currency
        });
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<OrderDto>();
        await RefreshAsync();

        return created is null ? null : GetOrder(created.Reference ?? string.Empty);
    }

    private int? ResolveAccountIdByCompany(string accountName)
    {
        var account = _accounts.FirstOrDefault(candidate =>
            string.Equals(candidate.Company, accountName, StringComparison.OrdinalIgnoreCase));

        if (account is not null && _accountIds.TryGetValue(account.Id, out int id))
        {
            return id;
        }

        return null;
    }

    public async Task<bool> AddQuoteLine(string quoteId, string sku, int quantity, int discountPct)
    {
        if (string.IsNullOrWhiteSpace(sku)) return false;

        // Resolve the SKU the operator picked to its database id; the catalog is the only place
        // that knows it, and the snapshot deliberately does not carry numeric ids.
        var catalog = await GetListAsync<ProductDto>("api/Product/Catalog");
        var match = catalog.FirstOrDefault(candidate =>
            string.Equals(candidate.Sku, sku, StringComparison.OrdinalIgnoreCase));
        if (match is null) return false;

        var response = await _client.PostAsJsonAsync(
            $"{_api}/api/Quote/{Uri.EscapeDataString(quoteId)}/Lines", new
            {
                ProductId = match.Id,
                Quantity = quantity < 1 ? 1 : quantity,
                ListPrice = match.RetailPrice,
                DiscountPct = discountPct
            });

        if (!response.IsSuccessStatusCode) return false;

        await RefreshAsync();

        return true;
    }

    // ---- Catalog mutations ----------------------------------------------------------------

    public async Task<Product?> AddProduct(Product draft)
    {
        // Products entered here belong to your own warehouse unless a distributor is named.
        bool ownStock = string.IsNullOrWhiteSpace(draft.Dist) || draft.Dist == "—";

        var response = await _client.PostAsJsonAsync($"{_api}/api/Product/Catalog", new
        {
            Sku = draft.Sku,
            ProductName = draft.Name,
            Description = string.Empty,
            Category = draft.Cat,
            Source = ownStock ? "Own" : "Distributor",
            Distributor = ownStock ? null : draft.Dist,
            DistributorSku = draft.DistSku == "—" ? null : draft.DistSku,
            Cost = draft.Cost,
            RetailPrice = draft.Price,
            QuantityInStock = draft.Avail,
            IsTaxable = true,
            ProductImage = draft.Image
        });
        response.EnsureSuccessStatusCode();

        await RefreshAsync();

        return GetProduct(draft.Sku);
    }

    public async Task UpdateProduct(Product edited)
    {
        var response = await _client.PutAsJsonAsync(
            $"{_api}/api/Product/Catalog/{Uri.EscapeDataString(edited.Sku)}", new
            {
                Sku = edited.Sku,
                ProductName = edited.Name,
                Description = string.Empty,
                Category = edited.Cat,
                Source = edited.Source,
                Distributor = edited.Dist == "—" ? null : edited.Dist,
                DistributorSku = edited.DistSku == "—" ? null : edited.DistSku,
                Cost = edited.Cost,
                RetailPrice = edited.Price,
                QuantityInStock = edited.Avail,
                IsTaxable = true,
                ProductImage = edited.Image
            });
        response.EnsureSuccessStatusCode();

        await RefreshAsync();
    }

    public async Task SyncFeeds()
    {
        var response = await _client.PostAsync($"{_api}/api/Product/Catalog/Sync", content: null);
        response.EnsureSuccessStatusCode();

        await RefreshAsync();
    }

    // ---- Distributor feed management -------------------------------------------------------

    public IReadOnlyList<DistributorFeedView> Feeds => _feeds;

    public async Task<DistributorFeedView?> SaveFeed(DistributorFeedView feed)
    {
        // Password is write-only: it is sent when the operator typed one, and simply omitted
        // otherwise so an edit cannot blank the stored credential.
        var body = ToFeedPayload(feed);

        var response = feed.Id == 0
            ? await _client.PostAsJsonAsync($"{_api}/api/DistributorFeed", body)
            : await _client.PutAsJsonAsync($"{_api}/api/DistributorFeed/{feed.Id}", body);

        response.EnsureSuccessStatusCode();

        await RefreshFeedsAsync();

        return _feeds.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, feed.Name, StringComparison.OrdinalIgnoreCase));
    }

    public async Task DeleteFeed(int id)
    {
        var response = await _client.DeleteAsync($"{_api}/api/DistributorFeed/{id}");
        response.EnsureSuccessStatusCode();

        await RefreshFeedsAsync();
    }

    public async Task<FeedSyncOutcome> TestFeed(DistributorFeedView feed)
    {
        var url = feed.Id == 0
            ? $"{_api}/api/DistributorFeed/Test"
            : $"{_api}/api/DistributorFeed/Test?id={feed.Id}";

        var response = await _client.PostAsJsonAsync(url, ToFeedPayload(feed));

        return await ReadOutcome(response, feed.Name);
    }

    public async Task<FeedSyncOutcome> SyncFeed(int id)
    {
        var response = await _client.PostAsync($"{_api}/api/DistributorFeed/{id}/Sync", content: null);
        var outcome = await ReadOutcome(response, null);

        // A sync changes the catalogue as well as the feed's status.
        await RefreshAsync();

        return outcome;
    }

    private static object ToFeedPayload(DistributorFeedView feed) => new
    {
        feed.Name,
        feed.Host,
        feed.Port,
        feed.Username,
        Password = feed.Password ?? string.Empty,
        feed.RemoteDirectory,
        feed.HostKeySha256,
        feed.Enabled,
        feed.FieldSku,
        feed.FieldName,
        feed.FieldDescription,
        feed.FieldCategory,
        feed.FieldCost,
        feed.FieldSrp,
        feed.FieldQuantity
    };

    private static async Task<FeedSyncOutcome> ReadOutcome(HttpResponseMessage response, string? name)
    {
        try
        {
            var outcome = await response.Content.ReadFromJsonAsync<FeedSyncOutcome>();
            if (outcome is not null) return outcome;
        }
        catch (Exception)
        {
            // Fall through to a generic failure below.
        }

        return new FeedSyncOutcome
        {
            Distributor = name,
            Succeeded = response.IsSuccessStatusCode,
            Error = response.IsSuccessStatusCode ? null : $"Request failed ({(int)response.StatusCode})."
        };
    }

    private async Task RefreshFeedsAsync()
    {
        Replace(_feeds, await GetListAsync<DistributorFeedView>("api/DistributorFeed"));
    }

    // ---- Group mutations -------------------------------------------------------------------

    public async Task<Group?> AddGroup(Group draft)
    {
        var response = await _client.PostAsJsonAsync($"{_api}/api/CustomerGroup", new
        {
            Name = draft.Name,
            Discount = draft.Discount,
            Terms = draft.Terms,
            Note = draft.Note
        });
        response.EnsureSuccessStatusCode();

        await RefreshAsync();

        return GetGroup(draft.Name);
    }

    public async Task UpdateGroup(string name, int discount, string terms, string note)
    {
        if (!_groupSlugs.TryGetValue(name, out var slug) || string.IsNullOrWhiteSpace(slug)) return;

        var response = await _client.PutAsJsonAsync($"{_api}/api/CustomerGroup/{Uri.EscapeDataString(slug)}", new
        {
            Discount = discount,
            Terms = terms,
            Note = note
        });
        response.EnsureSuccessStatusCode();

        await RefreshAsync();
    }

    // ---- User mutations ---------------------------------------------------------------------

    public async Task<User?> AddUser(string name, string email, string role)
    {
        var parts = (name ?? string.Empty).Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        var response = await _client.PostAsJsonAsync($"{_api}/api/User/Register", new
        {
            FirstName = parts.Length > 0 ? parts[0] : email,
            LastName = parts.Length > 1 ? parts[1] : string.Empty,
            Email = email,
            // A one-time value that satisfies the Identity password policy. The invited user
            // is expected to reset it — this is never shown or stored by the portal.
            Password = $"Aa1!{Guid.NewGuid():N}"
        });
        response.EnsureSuccessStatusCode();

        if (!string.IsNullOrWhiteSpace(role))
        {
            await UpdateUserRoles(email, new[] { role });
        }

        await RefreshAsync();

        return GetUser(email);
    }

    public async Task UpdateUserRoles(string email, IEnumerable<string> roles)
    {
        var target = roles?.Where(role => !string.IsNullOrWhiteSpace(role))
                         .ToHashSet(StringComparer.OrdinalIgnoreCase)
                     ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var users = await GetListAsync<AppUserDto>("api/User/Admin/GetAllUsers");
        var user = users.FirstOrDefault(candidate =>
            string.Equals(candidate.Email, email, StringComparison.OrdinalIgnoreCase));
        if (user is null) return;

        var current = user.Roles?.Values.ToHashSet(StringComparer.OrdinalIgnoreCase)
                      ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var role in target.Except(current, StringComparer.OrdinalIgnoreCase))
        {
            var add = await _client.PostAsJsonAsync($"{_api}/api/User/Admin/AddRole",
                new { UserId = user.UserId, RoleName = role });
            add.EnsureSuccessStatusCode();
        }

        foreach (var role in current.Except(target, StringComparer.OrdinalIgnoreCase))
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, $"{_api}/api/User/Admin/RemoveRole")
            {
                Content = JsonContent.Create(new { UserId = user.UserId, RoleName = role })
            };
            var remove = await _client.SendAsync(request);
            remove.EnsureSuccessStatusCode();
        }

        await RefreshAsync();
    }

    // ---- Plumbing ----------------------------------------------------------------------------

    private async Task<List<T>> GetListAsync<T>(string path)
    {
        try
        {
            return await _client.GetFromJsonAsync<List<T>>($"{_api}/{path}") ?? new List<T>();
        }
        catch (HttpRequestException)
        {
            // One unavailable endpoint should leave that section empty rather than take the
            // whole admin area down.
            return new List<T>();
        }
    }

    private static void Replace<T>(List<T> target, IEnumerable<T> items)
    {
        target.Clear();
        target.AddRange(items);
    }

    private static string Date(DateTime? value) => value?.ToString("dd MMM yyyy") ?? "—";

    private static Account MapAccount(AccountDto dto) => new()
    {
        Id = dto.Reference ?? dto.Id.ToString(),
        Company = dto.Company ?? string.Empty,
        Contact = dto.ContactName ?? string.Empty,
        Email = dto.Email ?? string.Empty,
        Country = dto.Country ?? string.Empty,
        Currency = dto.Currency ?? "EUR",
        Group = dto.GroupName ?? string.Empty,
        Payment = dto.PaymentMethod ?? string.Empty,
        Terms = dto.PaymentTerms ?? string.Empty,
        Credit = dto.CreditLimit,
        Status = dto.Status ?? "Pending",
        Since = Date(dto.CreatedDate)
    };

    private static Group MapGroup(GroupDto dto) => new()
    {
        Name = dto.Name ?? string.Empty,
        Discount = dto.Discount,
        Accounts = dto.Accounts,
        Overrides = dto.Overrides,
        Terms = dto.Terms ?? string.Empty,
        Note = dto.Note ?? string.Empty
    };

    private static Quote MapQuote(QuoteDto dto, List<QuoteLineDto> lines) => new()
    {
        Id = dto.Reference ?? string.Empty,
        Account = dto.AccountName ?? string.Empty,
        Currency = dto.Currency ?? "EUR",
        Value = dto.Value,
        Status = dto.Status ?? "Requested",
        Created = Date(dto.CreatedDate),
        Expires = Date(dto.ExpiresDate),
        Lines = dto.Lines,
        LineItems = lines.Select(line => new QuoteLine
        {
            Sku = line.Sku ?? string.Empty,
            Name = line.Name ?? string.Empty,
            Qty = line.Quantity,
            List = line.ListPrice,
            Disc = line.DiscountPct,
            Net = line.NetPrice
        }).ToList()
    };

    private static Order MapOrder(OrderDto dto, List<OrderLineDto> lines) => new()
    {
        Id = dto.Reference ?? string.Empty,
        Account = dto.AccountName ?? "—",
        Currency = dto.Currency ?? "EUR",
        Value = dto.FinalPrice,
        Status = dto.Status ?? "Awaiting payment",
        Placed = Date(dto.PurchaseDate),
        Items = dto.Items,
        From = dto.FromQuoteReference ?? "—",
        LineItems = lines.Select(line => new OrderLine
        {
            Sku = line.Sku ?? string.Empty,
            Name = line.Name ?? string.Empty,
            Qty = line.Quantity,
            Price = line.Price
        }).ToList()
    };

    private static Product MapProduct(ProductDto dto)
    {
        bool isDistributor = string.Equals(dto.Source, "Distributor", StringComparison.OrdinalIgnoreCase);

        return new Product
        {
            Sku = dto.Sku ?? dto.Id.ToString(),
            Name = dto.ProductName ?? string.Empty,
            Cat = dto.Category ?? "Components",
            Source = string.IsNullOrWhiteSpace(dto.Source) ? "Own" : dto.Source,
            Dist = dto.Distributor ?? "—",
            DistSku = dto.DistributorSku ?? "—",
            Cost = dto.Cost ?? 0m,
            Price = dto.RetailPrice,
            Avail = dto.QuantityInStock,
            Synced = dto.LastSynced.HasValue ? Date(dto.LastSynced) : "—",
            // A distributor line that has not synced within a day is flagged stale.
            Stale = isDistributor &&
                    (!dto.LastSynced.HasValue || dto.LastSynced.Value < DateTime.UtcNow.AddDays(-1)),
            Image = dto.ProductImage ?? string.Empty
        };
    }

    private static ReportRow MapReport(ReportDto dto) => new()
    {
        Date = Date(dto.Date),
        Account = dto.Account ?? "—",
        Ref = dto.Ref ?? string.Empty,
        Cur = dto.Currency ?? "EUR",
        Net = dto.Net,
        Vat = dto.Vat,
        Total = dto.Total
    };

    private static ActivityItem MapActivity(ActivityDto dto) => new()
    {
        When = Date(dto.When),
        Who = dto.Account ?? "—",
        What = dto.What ?? string.Empty,
        Type = dto.Type ?? string.Empty,
        Status = dto.Status ?? string.Empty,
        Screen = dto.Screen ?? "dashboard"
    };

    // Identity holds the login and its roles; dbo.User holds the staff display name. Joined
    // here so the users page can show both.
    private static IEnumerable<User> MapUsers(List<AppUserDto> logins, List<StaffDto> staff)
    {
        var names = staff
            .Where(member => !string.IsNullOrWhiteSpace(member.EmailAddress))
            .GroupBy(member => member.EmailAddress!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => $"{group.First().FirstName} {group.First().LastName}".Trim(),
                StringComparer.OrdinalIgnoreCase);

        return logins.Select(login => new User
        {
            Name = names.TryGetValue(login.Email ?? string.Empty, out var name) && !string.IsNullOrWhiteSpace(name)
                ? name
                : login.Email ?? string.Empty,
            Email = login.Email ?? string.Empty,
            Roles = login.Roles is { Count: > 0 } ? string.Join(", ", login.Roles.Values) : "Staff"
        });
    }

    // ---- API contracts -------------------------------------------------------------------------

    private sealed record AccountDto(int Id, string? Reference, string? Company, string? ContactName,
        string? Email, string? Country, string? Currency, int? CustomerGroupId, string? GroupName,
        string? PaymentMethod, string? PaymentTerms, decimal CreditLimit, string? Status, DateTime CreatedDate);

    private sealed record GroupDto(int Id, string? Name, string? Slug, int Discount, string? Terms,
        string? Note, int Accounts, int Overrides);

    private sealed record QuoteDto(int Id, string? Reference, int AccountId, string? AccountName,
        string? Currency, string? Status, DateTime CreatedDate, DateTime? ExpiresDate, int Lines, decimal Value);

    private sealed record QuoteLineDto(int Id, int QuoteId, int ProductId, string? Sku, string? Name,
        int Quantity, decimal ListPrice, int DiscountPct, decimal NetPrice);

    private sealed record OrderDto(int Id, string? Reference, int? AccountId, string? AccountName,
        string? Currency, string? Status, DateTime PurchaseDate, decimal SubTotal, decimal VAT,
        decimal FinalPrice, string? FromQuoteReference, int Items);

    private sealed record OrderLineDto(int Id, int PurchaseId, int ProductId, string? Sku, string? Name,
        int Quantity, decimal Price, decimal VAT);

    private sealed record ProductDto(int Id, string? ProductName, string? Description, decimal RetailPrice,
        int QuantityInStock, bool IsTaxable, string? ProductImage, string? Sku, string? Category,
        decimal? Cost, string? Source, string? Distributor, string? DistributorSku, DateTime? LastSynced);

    private sealed record ReportDto(DateTime Date, string? Account, string? Ref, string? Currency,
        decimal Net, decimal Vat, decimal Total);

    private sealed record ActivityDto(DateTime When, string? Account, string? What, string? Type,
        string? Status, string? Screen);

    private sealed record AppUserDto(string? UserId, string? Email, Dictionary<string, string>? Roles);

    private sealed record StaffDto(string? UserId, string? FirstName, string? LastName,
        string? EmailAddress, DateTime CreatedDate);
}
