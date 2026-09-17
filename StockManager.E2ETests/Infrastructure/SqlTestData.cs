using Microsoft.Data.SqlClient;

namespace StockManager.E2ETests.Infrastructure;

/// <summary>
/// The handful of rows these journeys need that nothing in the product can create yet, written
/// straight through Microsoft.Data.SqlClient rather than through StockApi or SMStore.
/// </summary>
/// <remarks>
/// Everywhere else in this project drives the real UI or the real API -- this class exists
/// only for the parts neither can reach: a store's own category taxonomy
/// (SiteCategory/CategoryMapping has no admin screen yet -- see the T3 plan) and a second Site
/// row (there is no "add a store" page either). Reference data, not workflow, which is why
/// going around the front door here is different from doing it for an account or a product.
/// </remarks>
public static class SqlTestData
{
    public sealed record SiteRecord(int Id, string SiteKey, string Name);

    public sealed record CategoryRecord(string FeedValue, string Slug);

    /// <summary>
    /// The site a request to <paramref name="host"/> resolves to, creating one if this is the
    /// first test in the run to ask -- which, on a freshly seeded database, is every request,
    /// since Scripts/PostDeployment/Seed.sql seeds exactly one row, for domain "localhost".
    /// </summary>
    /// <remarks>
    /// Keyed on the host rather than hardcoding "default" or "localhost": SiteResolver matches
    /// on Request.Host.Host (SMStore/Sites/SiteResolver.cs), so asking the database what
    /// already answers for that host is what keeps this correct whatever Aspire happens to
    /// bind sm-store's endpoint to, rather than assuming its port allocator and Seed.sql agree.
    ///
    /// A freshly created site's Name deliberately sorts after Seed.sql's "Default store": the
    /// admin portal's site switcher (SMPortal/Shared/Admin/AdminLayout.razor) defaults to the
    /// alphabetically-first store for a browser session that has never chosen one, and a
    /// journey that does not care which store it is acting for should keep landing on the
    /// seeded one rather than on whatever this method most recently created for another test.
    /// </remarks>
    public static async Task<SiteRecord> GetOrCreateSiteForHostAsync(
        string connectionString, string host, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var select = new SqlCommand(
            "SELECT [Id], [SiteKey], [Name] FROM dbo.Site WHERE [Domain] = @Domain", connection))
        {
            select.Parameters.AddWithValue("@Domain", host);

            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return new SiteRecord(reader.GetInt32(0), reader.GetString(1), reader.GetString(2));
            }
        }

        var siteKey = $"e2e-{Guid.NewGuid():N}";
        var name = $"Zzz E2E store ({host})";

        await using var insert = new SqlCommand(
            """
            INSERT INTO dbo.Site
                ([SiteKey], [Name], [Domain], [Country], [CurrencyCode], [Locale],
                 [OrderMode], [RegistrationFieldSet], [PriceDisplay], [IsActive])
            OUTPUT INSERTED.[Id]
            VALUES
                (@SiteKey, @Name, @Domain, 'IE', 'EUR', 'en-IE', 'Rfq', 'eu-b2b', 'Public', 1);
            """, connection);

        insert.Parameters.AddWithValue("@SiteKey", siteKey);
        insert.Parameters.AddWithValue("@Name", name);
        insert.Parameters.AddWithValue("@Domain", host);

        var id = (int)(await insert.ExecuteScalarAsync(cancellationToken))!;

        return new SiteRecord(id, siteKey, name);
    }

    /// <summary>
    /// A store category mapped to a fresh, GUID-named feed value, so a product filed under it
    /// cannot collide with a real distributor feed's category strings or with another test's.
    /// </summary>
    /// <remarks>
    /// dbo.Product carries no SiteId of its own -- dbo.CategoryMapping is what puts a product
    /// in a store's catalog at all (see CategoryMapping.sql and
    /// dbo.fnCatalog_VisibleProducts's inner join on it) -- so a product with no mapped
    /// category is invisible everywhere, which is why every journey that touches the
    /// storefront catalog calls this first.
    /// </remarks>
    public static async Task<CategoryRecord> CreateCategoryMappingAsync(
        string connectionString, int siteId, CancellationToken cancellationToken = default)
    {
        var unique = Guid.NewGuid().ToString("N");
        var feedValue = $"E2E-{unique}";
        var slug = $"e2e-{unique}";

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(
            """
            INSERT INTO dbo.SiteCategory ([SiteId], [Slug], [Name], [SortOrder], [IsActive])
            VALUES (@SiteId, @Slug, @Name, 0, 1);

            INSERT INTO dbo.CategoryMapping ([SiteId], [FeedValue], [SiteCategoryId])
            VALUES (@SiteId, @FeedValue, SCOPE_IDENTITY());
            """, connection);

        command.Parameters.AddWithValue("@SiteId", siteId);
        command.Parameters.AddWithValue("@Slug", slug);
        command.Parameters.AddWithValue("@Name", $"E2E category {unique}");
        command.Parameters.AddWithValue("@FeedValue", feedValue);

        await command.ExecuteNonQueryAsync(cancellationToken);

        return new CategoryRecord(feedValue, slug);
    }

    /// <summary>
    /// One product, visible on the storefront the moment <paramref name="category"/> names a
    /// mapped feed value.
    /// </summary>
    /// <remarks>
    /// Inserted directly to stand in for a distributor feed sync or an admin's own-warehouse
    /// entry -- StockVisibilityJourneyTests is about an admin editing an existing catalog row,
    /// not about how that row first arrived, and ProductController.Create (Source = "Own")
    /// would only be the same write with extra steps.
    /// </remarks>
    public static async Task InsertProductAsync(
        string connectionString, string sku, string name, string category,
        decimal retailPrice, int quantityInStock, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(
            """
            INSERT INTO dbo.Product
                ([ProductName], [Description], [RetailPrice], [QuantityInStock], [Sku], [Category], [Source])
            VALUES
                (@Name, N'', @RetailPrice, @QuantityInStock, @Sku, @Category, N'Own');
            """, connection);

        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@RetailPrice", retailPrice);
        command.Parameters.AddWithValue("@QuantityInStock", quantityInStock);
        command.Parameters.AddWithValue("@Sku", sku);
        command.Parameters.AddWithValue("@Category", category);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// <paramref name="count"/> in-stock products under one fresh category, for a paging
    /// assertion that needs a known total rather than whatever else this database holds.
    /// </summary>
    public static async Task InsertProductsAsync(
        string connectionString, string skuPrefix, string category, int count,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        for (var i = 0; i < count; i++)
        {
            await using var command = new SqlCommand(
                """
                INSERT INTO dbo.Product
                    ([ProductName], [Description], [RetailPrice], [QuantityInStock], [Sku], [Category], [Source])
                VALUES
                    (@Name, N'', 10.00, 5, @Sku, @Category, N'Own');
                """, connection);

            command.Parameters.AddWithValue("@Name", $"{skuPrefix} item {i:D3}");
            command.Parameters.AddWithValue("@Sku", $"{skuPrefix}-{i:D3}");
            command.Parameters.AddWithValue("@Category", category);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// A trading account and its one contact, written already Approved/Active -- the two
    /// columns CustomerSessionValidator and the storefront sign-in form both check (see
    /// spAccount_Approve and spAccount_Register) -- for a journey that needs a signed-in
    /// customer but is not itself exercising registration or approval.
    /// </summary>
    /// <remarks>
    /// RegistrationApprovalJourneyTests is what exercises the real form and the real approval
    /// screen; CrossTenantRefusalJourneyTests is testing what a session may do once it exists,
    /// and going through the full UI flow again to get there would just be the same journey
    /// run twice under a different name.
    /// </remarks>
    /// <summary>
    /// The <c>dbo.User</c> profile row that goes with an Identity login for a staff user.
    /// </summary>
    /// <remarks>
    /// An admin needs a row in both databases, and the second one is easy to miss because
    /// nothing fails until after a successful sign-in. <c>GET /api/User</c> looks the caller
    /// up by their Identity id and answers 404 when there is no profile; SMPortal's
    /// AuthStateProvider treats any failure of that call as a failed sign-in, so a token that
    /// was issued perfectly well is discarded and the operator is told "Check your email and
    /// password". A login with no profile therefore looks exactly like a wrong password.
    /// </remarks>
    public static async Task CreateUserProfileAsync(
        string connectionString, string identityUserId, string email,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(
            """
            INSERT INTO dbo.[User] ([UserId], [FirstName], [LastName], [EmailAddress])
            VALUES (@UserId, N'E2E', N'Admin', @EmailAddress);
            """, connection);

        command.Parameters.AddWithValue("@UserId", identityUserId);
        command.Parameters.AddWithValue("@EmailAddress", email);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task CreateApprovedAccountAndContactAsync(
        string connectionString, int siteId, string identityUserId, string email,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // "E2E" plus 32 hex characters would overflow Reference's NVARCHAR(20), hence the cut.
        var reference = $"E2E{Guid.NewGuid():N}"[..20];

        await using var account = new SqlCommand(
            """
            INSERT INTO dbo.Account ([Reference], [Company], [Email], [Status], [SiteId])
            OUTPUT INSERTED.[Id]
            VALUES (@Reference, @Company, @Email, N'Approved', @SiteId);
            """, connection);

        account.Parameters.AddWithValue("@Reference", reference);
        account.Parameters.AddWithValue("@Company", $"E2E Co {reference}");
        account.Parameters.AddWithValue("@Email", email);
        account.Parameters.AddWithValue("@SiteId", siteId);

        var accountId = (int)(await account.ExecuteScalarAsync(cancellationToken))!;

        await using var contact = new SqlCommand(
            """
            INSERT INTO dbo.Contact
                ([AccountId], [IdentityUserId], [FirstName], [LastName], [Email], [IsPrimary], [Status])
            VALUES
                (@AccountId, @IdentityUserId, N'E2E', N'Customer', @Email, 1, N'Active');
            """, connection);

        contact.Parameters.AddWithValue("@AccountId", accountId);
        contact.Parameters.AddWithValue("@IdentityUserId", identityUserId);
        contact.Parameters.AddWithValue("@Email", email);

        await contact.ExecuteNonQueryAsync(cancellationToken);
    }
}
