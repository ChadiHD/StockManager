using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;
using OutboxDb = SMDataManager.Library.Tests.EmailOutboxTests.OutboxDb;

namespace SMDataManager.Library.Tests;

/// <summary>
/// spSite_Delete: a store that never traded goes with its own set-up, and one that has stays.
/// </summary>
/// <remarks>
/// Every refusal is raised before the procedure opens its transaction, so each is safe anywhere
/// in the rollback scope. The one delete that succeeds commits only its own nested transaction.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class SiteDeleteTests
{
    [SkippableFact]
    public void AnOpenStoreIsRefused()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new OutboxDb(connection, transaction);

            db.Invoking(d => Delete(d))
                .Should().Throw<SqlException>().Which.Message.Should().Contain("Close it before removing it");

            StoreExists(db).Should().BeTrue();
        }
    }

    [SkippableFact]
    public void AStoreThatTradedIsRefusedNamingWhatItHolds()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new OutboxDb(connection, transaction);
            Close(db);
            db.Quote(db.PendingAccount("buyer@remove.invalid"), requestedBy: null);

            db.Invoking(d => Delete(d))
                .Should().Throw<SqlException>().Which.Message.Should()
                .Contain("because it has customer accounts, quotes.").And.Contain("can stay closed");

            StoreExists(db).Should().BeTrue();
        }
    }

    [SkippableFact]
    public void AStoreThatNeverTradedGoesWithItsSetUpAndTheSharedProductStays()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new OutboxDb(connection, transaction);
            Close(db);

            // A category, its mapping and a product placed under it; a page, a wording, a rate, a
            // group with a rule, a staff grant and a visitor's basket.
            var product = db.Product();
            var site = ("@site", (object)db.SiteId);
            db.Execute("""
                INSERT INTO dbo.SiteProduct (SiteId, ProductId, Visibility, SiteCategoryId)
                SELECT @site, @product, N'Show', MIN(Id) FROM dbo.SiteCategory WHERE SiteId = @site;
                INSERT INTO dbo.SiteContent (SiteId, ContentKey, Title, BodyHtml) VALUES (@site, N'terms', N'Terms', N'<p>Terms</p>');
                INSERT INTO dbo.SiteEmailTemplate (SiteId, TemplateKey, [Subject]) VALUES (@site, N'account.approved', N'Welcome');
                INSERT INTO dbo.ShippingRate (SiteId, Name, Price) VALUES (@site, N'Standard', 5);
                INSERT INTO dbo.CustomerGroup (Name, Slug, SiteId) VALUES (N'Reseller', N'reseller', @site);
                INSERT INTO dbo.GroupVisibility (CustomerGroupId, [Rule], [Value])
                SELECT Id, N'ExcludeDistributor', N'Nobody' FROM dbo.CustomerGroup WHERE SiteId = @site;
                INSERT INTO dbo.UserSite (UserId, SiteId) VALUES (@staff, @site);
                INSERT INTO dbo.Basket (SiteId, Token) VALUES (@site, @token);
                INSERT INTO dbo.BasketLine (BasketId, ProductId, Quantity) VALUES (SCOPE_IDENTITY(), @product, 1);
                """,
                site, ("@product", product), ("@staff", db.StaffId), ("@token", Guid.NewGuid().ToString("N")));

            Delete(db);

            StoreExists(db).Should().BeFalse();
            foreach (var table in new[] { "SiteCategory", "CategoryMapping", "SiteProduct", "SiteContent",
                         "SiteEmailTemplate", "ShippingRate", "CustomerGroup", "UserSite", "Basket" })
            {
                db.Scalar($"SELECT COUNT(*) FROM dbo.[{table}] WHERE SiteId = @site", site)
                    .Should().Be(0, $"{table} is the store's own set-up");
            }

            db.Scalar("SELECT COUNT(*) FROM dbo.Product WHERE Id = @product", ("@product", product))
                .Should().Be(1, "products are shared, and another store may sell this one");
        }
    }

    private static void Delete(OutboxDb db) =>
        db.Execute("EXEC dbo.spSite_Delete @SiteId = @site;", ("@site", db.SiteId));

    private static void Close(OutboxDb db) =>
        db.Execute("UPDATE dbo.Site SET IsActive = 0 WHERE Id = @site;", ("@site", db.SiteId));

    private static bool StoreExists(OutboxDb db) =>
        db.Scalar("SELECT COUNT(*) FROM dbo.Site WHERE Id = @site", ("@site", db.SiteId)) == 1;
}
