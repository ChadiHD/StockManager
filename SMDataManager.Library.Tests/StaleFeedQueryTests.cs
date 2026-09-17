using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// Which feeds <c>spDistributorFeed_GetStale</c> calls quiet.
/// </summary>
/// <remarks>
/// Against a real database because the whole procedure is a predicate over NULLs and a
/// subquery: a feed that has never synced, a threshold of zero, and a disabled feed are three
/// separate ways to get a wrong row set without getting an error. The alert built on top of it
/// is only as good as this answer, and an alert that names the wrong feed is worse than none.
///
/// Skips without SMDATABASE_TEST_CONNECTION; see <see cref="TestDatabase"/>.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class StaleFeedQueryTests
{
    [SkippableTheory]
    // Nothing is stale while the store has set no threshold. This is how every existing store
    // is configured, so the feature lands inert.
    [InlineData(0, new string[0])]
    // With a threshold, both the long-overdue feed and the one that never synced at all.
    [InlineData(26, new[] { "Never", "Stale" })]
    // A threshold longer than the gap catches nothing but the never-synced feed.
    [InlineData(100, new[] { "Never" })]
    public void OnlyEnabledFeedsPastTheStoresThresholdAreQuiet(int staleAfterHours, string[] expected)
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var siteId = CreateSite(connection, transaction, staleAfterHours);

            AddFeed(connection, transaction, siteId, "Fresh", enabled: true, syncedHoursAgo: 2);
            AddFeed(connection, transaction, siteId, "Stale", enabled: true, syncedHoursAgo: 48);
            AddFeed(connection, transaction, siteId, "Never", enabled: true, syncedHoursAgo: null);
            // Disabled and ancient. An operator who turned a feed off does not need telling
            // that it stopped delivering.
            AddFeed(connection, transaction, siteId, "Disabled", enabled: false, syncedHoursAgo: 500);

            Stale(connection, transaction, siteId).Should().BeEquivalentTo(expected);
        }
    }

    [SkippableFact]
    public void HidingStaleProductsIsNotWhatDecidesWhoIsTold()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            // HideStaleProducts left off: this store keeps selling stale stock while it chases
            // the distributor, and still wants to know. The two switches answer different
            // questions and only FeedStaleAfterHours governs this one.
            var siteId = CreateSite(connection, transaction, staleAfterHours: 12, hideStale: false);

            AddFeed(connection, transaction, siteId, "Stale", enabled: true, syncedHoursAgo: 48);

            Stale(connection, transaction, siteId).Should().BeEquivalentTo(["Stale"]);
        }
    }

    [SkippableFact]
    public void AnotherStoresQuietFeedIsNotThisStoresProblem()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var mine = CreateSite(connection, transaction, staleAfterHours: 12);
            var theirs = CreateSite(connection, transaction, staleAfterHours: 12);

            AddFeed(connection, transaction, theirs, "TheirStale", enabled: true, syncedHoursAgo: 48);

            // The alert built on this names a distributor and quotes a status message. Leaking
            // one store's to another store's operator is a disclosure, not a display bug.
            Stale(connection, transaction, mine).Should().BeEmpty();
            Stale(connection, transaction, theirs).Should().BeEquivalentTo(["TheirStale"]);
        }
    }

    private static List<string> Stale(SqlConnection connection, SqlTransaction transaction, int siteId)
    {
        using var command = new SqlCommand(
            "EXEC dbo.spDistributorFeed_GetStale @SiteId = @siteId;", connection, transaction);

        command.Parameters.AddWithValue("@siteId", siteId);

        using var reader = command.ExecuteReader();

        var names = new List<string>();

        while (reader.Read())
        {
            names.Add(reader.GetString(reader.GetOrdinal("Name")));
        }

        return names;
    }

    private static int CreateSite(
        SqlConnection connection, SqlTransaction transaction,
        int staleAfterHours, bool hideStale = true)
    {
        // SiteKey and Domain are unique, and a rolled-back transaction still collides with a
        // concurrent one that has not rolled back yet.
        var runId = Guid.NewGuid().ToString("N")[..12];

        using var command = new SqlCommand("""
            INSERT INTO dbo.Site (SiteKey, Name, Domain, Country, CurrencyCode, Locale,
                                  OrderMode, RegistrationFieldSet, PriceDisplay, MinMarginPct,
                                  FeedStaleAfterHours, HideStaleProducts, IsActive)
            OUTPUT INSERTED.Id
            VALUES (@key, N'Stale feed test store', @domain, N'IE', N'EUR', N'en-IE',
                    N'Rfq', N'eu-b2b', N'Public', 0, @staleAfter, @hideStale, 1);
            """, connection, transaction);

        command.Parameters.AddWithValue("@key", $"stalefeed-{runId}");
        command.Parameters.AddWithValue("@domain", $"{runId}.stalefeed.invalid");
        command.Parameters.AddWithValue("@staleAfter", staleAfterHours);
        command.Parameters.AddWithValue("@hideStale", hideStale);

        return (int)command.ExecuteScalar()!;
    }

    private static void AddFeed(
        SqlConnection connection, SqlTransaction transaction,
        int siteId, string name, bool enabled, int? syncedHoursAgo)
    {
        using var command = new SqlCommand("""
            INSERT INTO dbo.DistributorFeed (Name, Host, Username, SiteId, Enabled, LastSyncedUtc)
            VALUES (@name, N'sftp.example.test', N'u', @siteId, @enabled, @lastSynced);
            """, connection, transaction);

        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@siteId", siteId);
        command.Parameters.AddWithValue("@enabled", enabled);
        command.Parameters.AddWithValue("@lastSynced",
            syncedHoursAgo is { } hours ? DateTime.UtcNow.AddHours(-hours) : DBNull.Value);

        command.ExecuteNonQuery();
    }
}
