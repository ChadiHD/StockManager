using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;
using OutboxDb = SMDataManager.Library.Tests.EmailOutboxTests.OutboxDb;

namespace SMDataManager.Library.Tests;

/// <summary>
/// What the nightly sweep deletes, and what it must never delete.
/// </summary>
/// <remarks>
/// The sweep is global, so these rows are dated in 1999 and the cutoff is 2000: a development
/// database's own baskets and mail are all later, so nothing but the fixture's rows qualify and
/// the counts can be asserted exactly. Everything still rolls back.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class HousekeepingTests
{
    private static readonly DateTime Cutoff = new(2000, 1, 1);
    private static readonly DateTime Before = new(1999, 6, 1);
    private static readonly DateTime After = new(2000, 6, 1);

    [SkippableFact]
    public void OnlyAnonymousBasketsPastTheCutoffGo()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new OutboxDb(connection, transaction);
            var product = db.Product();
            var contact = db.Contact(db.PendingAccount("office@sweep.invalid"), "buyer@sweep.invalid");

            var abandoned = Basket(db, contactId: null, Before);
            Line(db, abandoned, product);
            var recent = Basket(db, contactId: null, After);
            // Old, but somebody's. It is what they see when they sign in again.
            var customers = Basket(db, contact, Before);

            var swept = Sweep(transaction, basketsBefore: Cutoff, mailBefore: Cutoff);

            swept.Baskets.Should().Be(1);
            Exists(db, "Basket", abandoned).Should().BeFalse();
            db.Scalar("SELECT COUNT(*) FROM dbo.BasketLine WHERE BasketId = @id", ("@id", abandoned))
                .Should().Be(0, "its lines go with it");
            Exists(db, "Basket", recent).Should().BeTrue();
            Exists(db, "Basket", customers).Should().BeTrue();
        }
    }

    [SkippableFact]
    public void OnlySentMailPastTheCutoffGoesAndDeadLettersStay()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new OutboxDb(connection, transaction);

            var oldSent = Mail(db, "Sent", Before);
            var newSent = Mail(db, "Sent", After);
            // An operator has to see this one, however old.
            var deadLetter = Mail(db, "DeadLettered", Before);
            var pending = Mail(db, "Pending", sentUtc: null);

            var swept = Sweep(transaction, basketsBefore: Cutoff, mailBefore: Cutoff);

            swept.Mail.Should().Be(1);
            Exists(db, "EmailOutbox", oldSent).Should().BeFalse();
            Exists(db, "EmailOutbox", newSent).Should().BeTrue();
            Exists(db, "EmailOutbox", deadLetter).Should().BeTrue();
            Exists(db, "EmailOutbox", pending).Should().BeTrue();
        }
    }

    [SkippableFact]
    public void ABacklogLargerThanABatchIsClearedInOnePass()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var db = new OutboxDb(connection, transaction);
            var ids = Enumerable.Range(0, 5).Select(_ => Basket(db, contactId: null, Before)).ToList();

            // A batch of two over five rows: the loop has to go round three times.
            var swept = Sweep(transaction, basketsBefore: Cutoff, mailBefore: Cutoff, batchSize: 2);

            swept.Baskets.Should().Be(5);
            ids.Should().OnlyContain(id => !Exists(db, "Basket", id));
        }
    }

    private static (int Baskets, int Mail) Sweep(SqlTransaction transaction, DateTime basketsBefore, DateTime mailBefore, int batchSize = 5000)
    {
        using var command = new SqlCommand("dbo.spHousekeeping_Sweep", transaction.Connection, transaction)
        {
            CommandType = System.Data.CommandType.StoredProcedure
        };
        command.Parameters.AddWithValue("@AbandonedBasketsBeforeUtc", basketsBefore);
        command.Parameters.AddWithValue("@SentMailBeforeUtc", mailBefore);
        command.Parameters.AddWithValue("@BatchSize", batchSize);

        using var reader = command.ExecuteReader();
        reader.Read();

        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    private static int Basket(OutboxDb db, int? contactId, DateTime updatedUtc) => db.Scalar("""
        INSERT INTO dbo.Basket (SiteId, ContactId, Token, CreatedUtc, UpdatedUtc)
        OUTPUT INSERTED.Id
        VALUES (@site, @contact, @token, @updated, @updated);
        """,
        ("@site", db.SiteId),
        ("@contact", (object?)contactId ?? DBNull.Value),
        ("@token", Guid.NewGuid().ToString("N")),
        ("@updated", updatedUtc));

    private static void Line(OutboxDb db, int basketId, int productId) => db.Execute(
        "INSERT INTO dbo.BasketLine (BasketId, ProductId, Quantity) VALUES (@basket, @product, 1);",
        ("@basket", basketId), ("@product", productId));

    private static int Mail(OutboxDb db, string status, DateTime? sentUtc)
    {
        var id = db.Enqueue();

        db.Execute("UPDATE dbo.EmailOutbox SET Status = @status, SentUtc = @sent WHERE Id = @id;",
            ("@status", status), ("@sent", (object?)sentUtc ?? DBNull.Value), ("@id", id));

        return id;
    }

    private static bool Exists(OutboxDb db, string table, int id) =>
        db.Scalar($"SELECT COUNT(*) FROM dbo.[{table}] WHERE Id = @id", ("@id", id)) == 1;
}
