using Microsoft.Data.SqlClient;

namespace SMDataManager.Library.Tests;

/// <summary>
/// Connection to a deployed SMDatabase, for the tests that have to exercise real T-SQL.
/// </summary>
/// <remarks>
/// These are integration tests and unapologetically so. What they check is that a SQL
/// expression and a C# method agree, and there is no way to evaluate the SQL half without SQL
/// Server — a reimplementation in C# would be a third copy of the thing under test.
///
/// The connection string comes from SMDATABASE_TEST_CONNECTION, and the tests skip rather than
/// fail when it is unset, so a plain `dotnet test` on a machine with no database is quiet
/// instead of red. Point it at a development database: every test writes, and rolls back.
/// </remarks>
internal static class TestDatabase
{
    public const string EnvironmentVariable = "SMDATABASE_TEST_CONNECTION";

    public const string SkipReason =
        "Set " + EnvironmentVariable + " to a development SMDatabase connection string to run this. "
        + "Under the app host, read it from the Aspire dashboard's sql resource.";

    public static string? ConnectionString =>
        Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } value
            ? value
            : null;

    public static bool IsConfigured => ConnectionString is not null;

    /// <summary>
    /// Opens a connection with a transaction that the caller is expected never to commit.
    /// Everything these tests insert is scaffolding for one assertion and must not outlive it.
    /// </summary>
    public static (SqlConnection Connection, SqlTransaction Transaction) OpenRollbackScope()
    {
        var connection = new SqlConnection(ConnectionString);
        connection.Open();

        return (connection, connection.BeginTransaction());
    }
}
