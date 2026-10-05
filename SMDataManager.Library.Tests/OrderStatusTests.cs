using FluentAssertions;
using Microsoft.Data.SqlClient;
using SMDataManager.Library.Models;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// The statuses an order may hold, and the one <c>CK_Purchase_Status</c> admits that is not
/// a status at all.
/// </summary>
/// <remarks>
/// NULL is the interesting case and the reason this needs a database. <c>dbo.Purchase</c>
/// does double duty: <c>spPurchase_Insert</c> writes a desktop POS sale with no reference, no
/// account and no status, and a constraint that forgot to admit NULL would take the till out
/// of service on the next publish — after the schema had already been applied.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class OrderStatusTests
{
    [Theory]
    [InlineData("Awaiting payment")]
    [InlineData("Processing")]
    [InlineData("Fulfilled")]
    [InlineData("Cancelled")]
    public void EveryStatusTheConstraintAllowsIsKnown(string status) =>
        OrderStatus.IsKnown(status).Should().BeTrue();

    [Theory]
    // Stricter than the constraint, which compares under the database collation and would
    // accept "fulfilled". The portal's filters and its progress bar compare with == in C#, so
    // a differently-cased status the database accepted matches no filter and no step.
    [InlineData("fulfilled")]
    [InlineData("Shipped")]
    [InlineData("")]
    public void AStatusTheUiCouldNotRenderIsNotKnown(string status) =>
        OrderStatus.IsKnown(status).Should().BeFalse();

    [SkippableFact]
    public void ADesktopSaleWithNoStatusIsStillAllowed()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            // No Reference, no Account, no Status — the shape spPurchase_Insert writes, and
            // the shape every spOrder_* procedure filters out with Reference IS NOT NULL.
            Insert(connection, transaction, status: null).Should().BeGreaterThan(0);
        }
    }

    [SkippableFact]
    public void AStatusNobodyNamedIsRefused()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var insert = () => Insert(connection, transaction, status: "Shipped");

            insert.Should().Throw<SqlException>().WithMessage("*CK_Purchase_Status*");
        }
    }

    private static int Insert(
        SqlConnection connection, SqlTransaction transaction, string? status)
    {
        var runId = Guid.NewGuid().ToString("N")[..12];
        var staffId = $"staff-{runId}";

        // A till sale has a staff member, and CK_Purchase_Placer demands exactly one placer,
        // so this cannot be skipped to keep the fixture short: StaffId is a foreign key into
        // dbo.[User] and a POS row with neither placer is the row that constraint exists to
        // refuse.
        using var user = new SqlCommand("""
            INSERT INTO dbo.[User] (UserId, FirstName, LastName, EmailAddress)
            VALUES (@staffId, N'Status', N'Fixture', @email);
            """, connection, transaction);

        user.Parameters.AddWithValue("@staffId", staffId);
        user.Parameters.AddWithValue("@email", $"{runId}@status.invalid");
        user.ExecuteNonQuery();

        using var command = new SqlCommand("""
            INSERT INTO dbo.Purchase ([StaffId], [PurchaseDate], [SubTotal], [VAT],
                                      [FinalPrice], [Status])
            OUTPUT INSERTED.Id
            VALUES (@staffId, SYSUTCDATETIME(), 0, 0, 0, @status);
            """, connection, transaction);

        command.Parameters.AddWithValue("@staffId", staffId);
        command.Parameters.AddWithValue("@status", (object?)status ?? DBNull.Value);

        return (int)command.ExecuteScalar()!;
    }
}
