using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// Which products a store sells, decided per store, and the same answer on every path a
/// customer can reach a product by.
/// </summary>
/// <remarks>
/// A database test because the rule is <c>dbo.fnSite_ProductPlacement</c> applied inside
/// <c>dbo.fnCatalog_VisibleProducts</c>, and what is worth asserting is that the listing, the
/// facets, the detail page, the basket and the submit all agree. A product hidden from one of
/// them and reachable through another is a product the store did not mean to sell.
///
/// Two stores share every product here, because <c>dbo.Product</c> is shared: the point of
/// T8 is that one store's choice stops at that store.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class StorePlacementTests
{
    private const int NotAvailableInThisStore = 50021;
    private const int NotSoldByThisStore = 50031;

    [SkippableFact]
    public void PlacementSaysWhyAProductIsOrIsNotOnAStore()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var f = PlacementFixture.Create(connection, transaction);

            f.Placement(f.StoreA, f.Mapped).Should().Be(("Mapped", true));
            f.Placement(f.StoreA, f.Unmapped).Should().Be(("Unmapped", false));

            f.Override(f.StoreA, f.Mapped, "Hide");
            f.Placement(f.StoreA, f.Mapped).Should().Be(("Hidden", false));

            // Shown with nowhere to file it is still not on the store: the catalog joins a
            // category for slugs, facets and breadcrumbs, and there is none.
            f.Override(f.StoreA, f.Unmapped, "Show");
            f.Placement(f.StoreA, f.Unmapped).Should().Be(("Unmapped", false));

            f.Override(f.StoreA, f.Unmapped, "Show", f.OtherCategoryA);
            f.Placement(f.StoreA, f.Unmapped).Should().Be(("Shown", true));

            // None of which reached the other store.
            f.Placement(f.StoreB, f.Mapped).Should().Be(("Mapped", true));
            f.Placement(f.StoreB, f.Unmapped).Should().Be(("Unmapped", false));
        }
    }

    [SkippableFact]
    public void AHiddenProductIsGoneFromTheListingFacetsAndDetailPage_OnThatStoreOnly()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var f = PlacementFixture.Create(connection, transaction);
            f.Override(f.StoreA, f.Mapped, "Hide");

            f.SearchSkus(f.StoreA).Should().NotContain(f.MappedSku).And.Contain(f.SecondSku);
            // Counted the same way it is listed: a facet that still counted it would offer a
            // page one product shorter than it says.
            f.CategoryCount(f.StoreA).Should().Be(1);
            f.DetailFound(f.StoreA, f.MappedSku).Should().BeFalse();

            f.SearchSkus(f.StoreB).Should().Contain(f.MappedSku);
            f.CategoryCount(f.StoreB).Should().Be(2);
            f.DetailFound(f.StoreB, f.MappedSku).Should().BeTrue();
        }
    }

    [SkippableFact]
    public void AHiddenProductCannotBeAddedToABasket()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var f = PlacementFixture.Create(connection, transaction);
            var basketId = f.Basket(f.StoreA);
            f.Override(f.StoreA, f.Mapped, "Hide");

            // Raised before the procedure opens its transaction, so safe mid-scope. A product
            // id arrives in a form post; the page it came from is not evidence.
            var add = () => f.AddLine(basketId, f.StoreA, f.Mapped);

            add.Should().Throw<SqlException>().Which.Number.Should().Be(NotAvailableInThisStore);
        }
    }

    [SkippableFact]
    public void AProductHiddenAfterItWasAddedIsUnavailableAndRefusedAtSubmit()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var f = PlacementFixture.Create(connection, transaction);
            var basketId = f.Basket(f.StoreA);
            f.AddLine(basketId, f.StoreA, f.Mapped);

            f.Override(f.StoreA, f.Mapped, "Hide");

            // Kept and flagged rather than dropped, as for a delisted product, so the basket
            // page can say what happened instead of losing a row.
            f.LineAvailable(basketId, f.StoreA, f.Mapped).Should().BeFalse();

            // spQuote_SubmitRequest checks every line on its own terms, through the same
            // placement. Thrown before its transaction opens.
            var submit = () => f.Submit(f.StoreA, f.Mapped);

            submit.Should().Throw<SqlException>().Which.Number.Should().Be(NotSoldByThisStore);
        }
    }

    [SkippableFact]
    public void AShownProductAppearsUnderTheCategoryItWasGiven()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var f = PlacementFixture.Create(connection, transaction);
            f.Override(f.StoreA, f.Unmapped, "Show", f.OtherCategoryA);

            f.SearchSkus(f.StoreA, f.OtherCategorySlugA).Should().Equal(f.UnmappedSku);
            f.DetailFound(f.StoreA, f.UnmappedSku).Should().BeTrue();

            // Store B never mapped that feed category and made no choice of its own.
            f.SearchSkus(f.StoreB).Should().NotContain(f.UnmappedSku);
        }
    }

    [SkippableFact]
    public void HidingAProductDoesNotWithdrawItFromAQuoteAlreadyPriced()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var f = PlacementFixture.Create(connection, transaction);
            var quoteId = f.PricedQuote(f.StoreA, f.Mapped);

            f.Override(f.StoreA, f.Mapped, "Hide");

            // The DelistedProductHistoryTests rule, for a store's own choice. The price was
            // quoted; hiding the product from the shop window is not withdrawing it.
            f.QuoteLineCount(quoteId, f.StoreA).Should().Be(1);
            f.Convert(quoteId, f.StoreA);
            f.OrderLineCount(quoteId).Should().Be(1);
        }
    }

    [SkippableFact]
    public void FeaturedAndBadgeAreTheStoresOwn()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var f = PlacementFixture.Create(connection, transaction);
            f.Override(f.StoreA, f.Second, visibility: null, featured: true, badge: "New");

            // Featured is the default sort, so it leads store A's listing; store B never
            // featured it and lists by its own order.
            var storeA = f.Search(f.StoreA);
            storeA.First().Should().Be((f.SecondSku, "New"));

            f.Search(f.StoreB).Should().Contain((f.SecondSku, null))
                .And.NotContain(row => row.Badge == "New");
        }
    }

    // --- The admin's writes -------------------------------------------------------------------

    [SkippableFact]
    public void AStoreCannotFileAProductUnderAnotherStoresCategory()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var f = PlacementFixture.Create(connection, transaction);

            // Store B naming store A's category. The composite key would refuse it as well,
            // from inside the procedure, as a 500; this is the sentence the admin is shown.
            var set = () => f.Set(f.StoreB, f.UnmappedSku, "Show", f.OtherCategoryA);

            set.Should().Throw<SqlException>().Which.Number.Should().Be(50072);
        }
    }

    [SkippableFact]
    public void ShowingAProductWithNowhereToFileItIsRefused()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var f = PlacementFixture.Create(connection, transaction);

            // Accepted, it would be stored and do nothing: "Show" on a product no customer finds.
            var set = () => f.Set(f.StoreA, f.UnmappedSku, "Show", null);

            set.Should().Throw<SqlException>().Which.Number.Should().Be(50073);
        }
    }

    [SkippableFact]
    public void AChoiceThatMatchesTheDefaultsLeavesNoRowBehind()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var f = PlacementFixture.Create(connection, transaction);

            f.Set(f.StoreA, f.MappedSku, "Hide", null);
            f.RowCount(f.StoreA).Should().Be(1);

            // The table holds exceptions; a row saying "follow the mapping" says nothing.
            f.Set(f.StoreA, f.MappedSku, null, null);
            f.RowCount(f.StoreA).Should().Be(0);
            f.Placement(f.StoreA, f.Mapped).Should().Be(("Mapped", true));
        }
    }

    [SkippableFact]
    public void ABulkChangeAppliesToTheSelectionAndKeepsEachProductsRibbon()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var f = PlacementFixture.Create(connection, transaction);
            f.Set(f.StoreA, f.SecondSku, null, null, featured: true, badge: "New");

            f.SetVisibility(f.StoreA, "Hide", null, f.MappedSku, f.SecondSku, "NO-SUCH-SKU").Should().Be(2);
            f.Placement(f.StoreA, f.Mapped).Should().Be(("Hidden", false));
            f.Placement(f.StoreA, f.Second).Should().Be(("Hidden", false));

            // Handed back to the mapping. The plain one leaves no row; the one with a ribbon
            // keeps it, because a selection is about what is on sale, not how it is featured.
            f.SetVisibility(f.StoreA, null, null, f.MappedSku, f.SecondSku).Should().Be(2);
            f.Placement(f.StoreA, f.Second).Should().Be(("Mapped", true));
            f.RowCount(f.StoreA).Should().Be(1);
            f.Search(f.StoreA).Should().Contain((f.SecondSku, "New"));
        }
    }

    [SkippableFact]
    public void MappingAFeedCategoryPutsEveryProductInItOnTheStore_AndUnmappingTakesThemOff()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var f = PlacementFixture.Create(connection, transaction);

            f.Map(f.StoreA, f.UnmappedFeed, f.OtherCategoryA);
            f.SearchSkus(f.StoreA, f.OtherCategorySlugA).Should().Equal(f.UnmappedSku);

            f.Map(f.StoreA, f.MappedFeed, null);
            f.SearchSkus(f.StoreA).Should().NotContain(f.MappedSku).And.NotContain(f.SecondSku);
            // Store B's mapping is its own.
            f.SearchSkus(f.StoreB).Should().Contain(f.MappedSku);
        }
    }

    [SkippableFact]
    public void AStoreCannotMapAFeedCategoryToAnotherStoresCategory()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var f = PlacementFixture.Create(connection, transaction);

            var map = () => f.Map(f.StoreB, f.UnmappedFeed, f.OtherCategoryA);

            map.Should().Throw<SqlException>().Which.Number.Should().Be(50072);
        }
    }

    [SkippableFact]
    public void TheAdminsListSaysWhatTheStorefrontDoes()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var f = PlacementFixture.Create(connection, transaction);
            f.Set(f.StoreA, f.MappedSku, "Hide", null);
            f.Set(f.StoreA, f.UnmappedSku, "Show", f.OtherCategoryA);

            var list = f.AdminList(f.StoreA);

            list[f.MappedSku].Should().Be(("Hidden", false, null));
            list[f.SecondSku].Should().Be(("Mapped", true, "Mapped"));
            list[f.UnmappedSku].Should().Be(("Shown", true, "Other"));

            // And the shop agrees, row for row.
            f.SearchSkus(f.StoreA).Should().BeEquivalentTo(
                list.Where(row => row.Value.OnStore).Select(row => row.Key));
        }
    }

    /// <summary>Two stores, the same three products, and a category each.</summary>
    private sealed class PlacementFixture
    {
        private readonly SqlConnection _connection;
        private readonly SqlTransaction _transaction;
        private readonly string _runId;

        private PlacementFixture(SqlConnection connection, SqlTransaction transaction, string runId)
        {
            _connection = connection;
            _transaction = transaction;
            _runId = runId;
        }

        public int StoreA { get; private set; }
        public int StoreB { get; private set; }
        public int OtherCategoryA { get; private set; }
        public string OtherCategorySlugA => $"other-a-{_runId}";

        /// <summary>Filed under a feed category both stores map.</summary>
        public int Mapped { get; private set; }
        public int Second { get; private set; }

        /// <summary>Filed under a feed category neither store maps.</summary>
        public int Unmapped { get; private set; }

        public string MappedSku => $"PLC-{_runId}-1";
        public string SecondSku => $"PLC-{_runId}-2";
        public string UnmappedSku => $"PLC-{_runId}-3";

        public string MappedFeed => $"PlcMapped-{_runId}";
        public string UnmappedFeed => $"PlcUnmapped-{_runId}";

        public static PlacementFixture Create(SqlConnection connection, SqlTransaction transaction)
        {
            var f = new PlacementFixture(connection, transaction, Guid.NewGuid().ToString("N")[..10]);
            var mappedFeed = f.MappedFeed;

            f.StoreA = f.Store("a", mappedFeed);
            f.StoreB = f.Store("b", mappedFeed);

            f.OtherCategoryA = f.Scalar("""
                INSERT INTO dbo.SiteCategory (SiteId, Slug, Name, SortOrder, IsActive)
                OUTPUT INSERTED.Id VALUES (@site, @slug, N'Other', 2, 1);
                """, ("@site", f.StoreA), ("@slug", f.OtherCategorySlugA));

            f.Mapped = f.Product(f.MappedSku, mappedFeed);
            f.Second = f.Product(f.SecondSku, mappedFeed);
            f.Unmapped = f.Product(f.UnmappedSku, f.UnmappedFeed);

            return f;
        }

        public void Set(int siteId, string sku, string? visibility, int? categoryId,
            bool featured = false, string? badge = null) => Execute("""
            EXEC dbo.spSiteProduct_Set @SiteId = @site, @Sku = @sku, @Visibility = @visibility,
                 @SiteCategoryId = @category, @Featured = @featured, @Badge = @badge;
            """,
            ("@site", siteId), ("@sku", sku), ("@visibility", (object?)visibility ?? DBNull.Value),
            ("@category", (object?)categoryId ?? DBNull.Value), ("@featured", featured),
            ("@badge", (object?)badge ?? DBNull.Value));

        public int SetVisibility(int siteId, string? visibility, int? categoryId, params string[] skus)
        {
            // Built in T-SQL rather than passed as a table, as QuoteSubmissionTests does: what
            // is under test is the procedure, not Dapper's binding of the type.
            var values = string.Join(", ", skus.Select(sku => $"(N'{sku}')"));

            return Scalar($"""
                DECLARE @skus dbo.SkuList;
                INSERT INTO @skus VALUES {values};
                EXEC dbo.spSiteProduct_SetVisibility @SiteId = @site, @Skus = @skus,
                     @Visibility = @visibility, @SiteCategoryId = @category;
                """,
                ("@site", siteId), ("@visibility", (object?)visibility ?? DBNull.Value),
                ("@category", (object?)categoryId ?? DBNull.Value));
        }

        public void Map(int siteId, string feedValue, int? categoryId) => Execute(
            "EXEC dbo.spCategoryMapping_Set @SiteId = @site, @FeedValue = @feed, @SiteCategoryId = @category;",
            ("@site", siteId), ("@feed", feedValue), ("@category", (object?)categoryId ?? DBNull.Value));

        public int RowCount(int siteId) => Scalar(
            "SELECT COUNT(*) FROM dbo.SiteProduct WHERE SiteId = @site;", ("@site", siteId));

        public Dictionary<string, (string Placement, bool OnStore, string? StoreCategory)> AdminList(int siteId)
        {
            using var command = Command("EXEC dbo.spProduct_GetCatalogForSite @SiteId = @site;", ("@site", siteId));
            using var reader = command.ExecuteReader();
            var rows = new Dictionary<string, (string, bool, string?)>();

            while (reader.Read())
            {
                var sku = reader.GetString(reader.GetOrdinal("Sku"));

                if (!sku.Contains(_runId)) continue;

                var category = reader.GetOrdinal("StoreCategory");
                rows[sku] = (reader.GetString(reader.GetOrdinal("Placement")),
                             reader.GetBoolean(reader.GetOrdinal("OnStore")),
                             reader.IsDBNull(category) ? null : reader.GetString(category));
            }

            return rows;
        }

        private int Store(string letter, string mappedFeed)
        {
            var siteId = Scalar("""
                INSERT INTO dbo.Site (SiteKey, Name, Domain, Country, CurrencyCode, Locale,
                                      OrderMode, RegistrationFieldSet, PriceDisplay, MinMarginPct, IsActive)
                OUTPUT INSERTED.Id
                VALUES (@key, N'Placement test store', @domain, N'IE', N'EUR', N'en-IE',
                        N'Rfq', N'eu-b2b', N'Public', 0, 1);
                """,
                ("@key", $"plc-{letter}-{_runId}"), ("@domain", $"{letter}.{_runId}.placement.invalid"));

            var categoryId = Scalar("""
                INSERT INTO dbo.SiteCategory (SiteId, Slug, Name, SortOrder, IsActive)
                OUTPUT INSERTED.Id VALUES (@site, @slug, N'Mapped', 1, 1);
                """, ("@site", siteId), ("@slug", $"mapped-{letter}-{_runId}"));

            Execute("INSERT INTO dbo.CategoryMapping (SiteId, FeedValue, SiteCategoryId) VALUES (@site, @feed, @category);",
                ("@site", siteId), ("@feed", mappedFeed), ("@category", categoryId));

            return siteId;
        }

        private int Product(string sku, string feedCategory) => Scalar("""
            INSERT INTO dbo.Product (ProductName, [Description], RetailPrice, Sku, Category,
                                     QuantityInStock, Delisted)
            OUTPUT INSERTED.Id
            VALUES (@sku, N'Placement fixture.', 100, @sku, @category, 5, 0);
            """, ("@sku", sku), ("@category", feedCategory));

        public void Override(int siteId, int productId, string? visibility, int? categoryId = null,
            bool featured = false, string? badge = null) => Execute("""
            MERGE dbo.SiteProduct AS t
            USING (VALUES (@site, @product)) AS s (SiteId, ProductId)
                ON t.SiteId = s.SiteId AND t.ProductId = s.ProductId
            WHEN MATCHED THEN UPDATE SET Visibility = @visibility, SiteCategoryId = @category,
                                         Featured = @featured, Badge = @badge
            WHEN NOT MATCHED THEN INSERT (SiteId, ProductId, Visibility, SiteCategoryId, Featured, Badge)
                                  VALUES (@site, @product, @visibility, @category, @featured, @badge);
            """,
            ("@site", siteId), ("@product", productId), ("@visibility", (object?)visibility ?? DBNull.Value),
            ("@category", (object?)categoryId ?? DBNull.Value), ("@featured", featured),
            ("@badge", (object?)badge ?? DBNull.Value));

        public (string Reason, bool OnStore) Placement(int siteId, int productId)
        {
            using var command = Command("""
                SELECT pl.Reason, pl.OnStore
                FROM dbo.Product p
                CROSS APPLY dbo.fnSite_ProductPlacement(@site, p.Id, p.Category, p.FeedId, p.Source, p.CurrencyCode) pl
                WHERE p.Id = @product;
                """, ("@site", siteId), ("@product", productId));

            using var reader = command.ExecuteReader();
            reader.Read().Should().BeTrue();

            return (reader.GetString(0), reader.GetBoolean(1));
        }

        public List<(string Sku, string? Badge)> Search(int siteId, string? categorySlug = null)
        {
            using var command = Command(
                "EXEC dbo.spCatalog_Search @SiteId = @site, @CategorySlug = @slug, @PageSize = 100;",
                ("@site", siteId), ("@slug", (object?)categorySlug ?? DBNull.Value));

            using var reader = command.ExecuteReader();
            var rows = new List<(string, string?)>();

            while (reader.Read())
            {
                var sku = reader.GetString(reader.GetOrdinal("Sku"));

                // A store's own listing holds every other test fixture's products as well, so
                // only this run's rows are kept.
                if (sku.Contains(_runId))
                {
                    var badge = reader.GetOrdinal("Badge");
                    rows.Add((sku, reader.IsDBNull(badge) ? null : reader.GetString(badge)));
                }
            }

            return rows;
        }

        public List<string> SearchSkus(int siteId, string? categorySlug = null) =>
            Search(siteId, categorySlug).Select(row => row.Sku).ToList();

        public int CategoryCount(int siteId)
        {
            using var command = Command("EXEC dbo.spCatalog_GetFacets @SiteId = @site;", ("@site", siteId));
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                if (reader.GetString(reader.GetOrdinal("Slug")).StartsWith("mapped-"))
                {
                    return reader.GetInt32(reader.GetOrdinal("Count"));
                }
            }

            return 0;
        }

        public bool DetailFound(int siteId, string sku)
        {
            using var command = Command("EXEC dbo.spCatalog_GetBySku @SiteId = @site, @Sku = @sku;",
                ("@site", siteId), ("@sku", sku));
            using var reader = command.ExecuteReader();

            return reader.Read();
        }

        public int Basket(int siteId)
        {
            // 43 base64url characters, the shape BasketToken mints.
            var token = (_runId + new string('x', 43))[..43];

            using var command = Command("EXEC dbo.spBasket_Ensure @SiteId = @site, @Token = @token;",
                ("@site", siteId), ("@token", token));
            using var reader = command.ExecuteReader();
            reader.Read().Should().BeTrue();

            return reader.GetInt32(reader.GetOrdinal("Id"));
        }

        public void AddLine(int basketId, int siteId, int productId) => Execute(
            "EXEC dbo.spBasket_AddLine @BasketId = @basket, @SiteId = @site, @ProductId = @product, @Quantity = 1;",
            ("@basket", basketId), ("@site", siteId), ("@product", productId));

        public bool LineAvailable(int basketId, int siteId, int productId)
        {
            using var command = Command("EXEC dbo.spBasket_GetLines @BasketId = @basket, @SiteId = @site;",
                ("@basket", basketId), ("@site", siteId));
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                if (reader.GetInt32(reader.GetOrdinal("ProductId")) == productId)
                {
                    return reader.GetBoolean(reader.GetOrdinal("Available"));
                }
            }

            throw new InvalidOperationException("The line should still be in the basket, flagged.");
        }

        private (int AccountId, int ContactId) Customer(int siteId)
        {
            var accountId = Scalar("""
                INSERT INTO dbo.Account (Reference, Company, Currency, PaymentMethod, PaymentTerms,
                                         PaymentTermsDays, CreditLimit, Status, SiteId)
                OUTPUT INSERTED.Id
                VALUES (@reference, N'Placement customer', N'EUR', N'Card', N'Prepaid', 0, 0,
                        N'Approved', @site);
                """, ("@reference", $"AC-P{_runId[..8]}"), ("@site", siteId));

            var contactId = Scalar("""
                INSERT INTO dbo.Contact (AccountId, FirstName, LastName, Email, RoleInAccount, IsPrimary, [Status])
                OUTPUT INSERTED.Id
                VALUES (@account, N'Placement', N'Buyer', @email, N'Buyer', 1, N'Active');
                """, ("@account", accountId), ("@email", $"buyer-{_runId}@placement.invalid"));

            return (accountId, contactId);
        }

        public void Submit(int siteId, int productId)
        {
            var (_, contactId) = Customer(siteId);

            Execute($"""
                DECLARE @lines dbo.QuoteRequestLine;
                INSERT INTO @lines VALUES ({productId}, 1, 100, 0, 100);
                DECLARE @Id int, @Reference nvarchar(20);
                EXEC dbo.spQuote_SubmitRequest @ContactId = @contact, @SiteId = @site,
                     @Lines = @lines, @Id = @Id OUTPUT, @Reference = @Reference OUTPUT;
                """, ("@contact", contactId), ("@site", siteId));
        }

        public int PricedQuote(int siteId, int productId)
        {
            var (accountId, _) = Customer(siteId);

            var quoteId = Scalar("""
                INSERT INTO dbo.Quote (Reference, AccountId, Currency, [Status], SiteId)
                OUTPUT INSERTED.Id VALUES (@reference, @account, N'EUR', N'Priced', @site);
                """, ("@reference", $"QT-P{_runId[..8]}"), ("@account", accountId), ("@site", siteId));

            Execute("""
                INSERT INTO dbo.QuoteLine (QuoteId, ProductId, Quantity, ListPrice, DiscountPct, NetPrice)
                VALUES (@quote, @product, 1, 100, 0, 100);
                """, ("@quote", quoteId), ("@product", productId));

            return quoteId;
        }

        public int QuoteLineCount(int quoteId, int siteId)
        {
            using var command = Command("EXEC dbo.spQuoteLine_GetByQuote @QuoteId = @quote, @SiteId = @site;",
                ("@quote", quoteId), ("@site", siteId));
            using var reader = command.ExecuteReader();
            var count = 0;

            while (reader.Read()) count++;

            return count;
        }

        public void Convert(int quoteId, int siteId)
        {
            var staffId = $"plc-staff-{_runId}";

            Execute("INSERT INTO dbo.[User] (UserId, FirstName, LastName, EmailAddress) VALUES (@id, N'P', N'S', @email);",
                ("@id", staffId), ("@email", $"{_runId}@placement-staff.invalid"));

            Execute("""
                DECLARE @Id int, @Reference nvarchar(20);
                EXEC dbo.spOrder_ConvertFromQuote @QuoteId = @quote, @Id = @Id OUTPUT,
                     @Reference = @Reference OUTPUT, @SiteId = @site, @StaffId = @staff;
                """, ("@quote", quoteId), ("@site", siteId), ("@staff", staffId));
        }

        public int OrderLineCount(int quoteId) => Scalar("""
            SELECT COUNT(*) FROM dbo.PurchaseDetail d
            INNER JOIN dbo.Purchase p ON p.Id = d.PurchaseId
            WHERE p.QuoteId = @quote;
            """, ("@quote", quoteId));

        private int Scalar(string sql, params (string Name, object Value)[] parameters)
        {
            using var command = Command(sql, parameters);

            return System.Convert.ToInt32(command.ExecuteScalar());
        }

        private void Execute(string sql, params (string Name, object Value)[] parameters)
        {
            using var command = Command(sql, parameters);

            command.ExecuteNonQuery();
        }

        private SqlCommand Command(string sql, params (string Name, object Value)[] parameters)
        {
            var command = new SqlCommand(sql, _connection, _transaction);

            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return command;
        }
    }
}
