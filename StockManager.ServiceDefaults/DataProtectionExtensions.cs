using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Holds the Data Protection key ring in the identity database so every app and every replica
/// reads the same keys.
/// </summary>
public class DataProtectionKeyContext : DbContext, IDataProtectionKeyContext
{
    public DataProtectionKeyContext(DbContextOptions<DataProtectionKeyContext> options)
        : base(options)
    {
    }

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
}

public static class DataProtectionExtensions
{
    // Both apps must agree on this, or each derives its own keys from the same ring and
    // neither can read the other's cookies and ciphertext.
    private const string SharedApplicationName = "StockManager";

    /// <summary>
    /// Persists Data Protection keys to the identity database, shared across StockApi and
    /// SMStore.
    /// <para>
    /// Without this, each app keeps its key ring on its own container filesystem. Two
    /// consequences, both silent: an auth cookie issued by one host is unreadable by the
    /// other, and a rebuilt container can no longer decrypt distributor feed credentials
    /// written before it (see <c>DataProtectionFeedSecretStore</c>).
    /// </para>
    /// </summary>
    public static TBuilder AddSharedDataProtection<TBuilder>(
        this TBuilder builder,
        string connectionStringName = "DefaultConnection")
        where TBuilder : IHostApplicationBuilder
    {
        var connectionString = builder.Configuration.GetConnectionString(connectionStringName);

        builder.Services.AddDbContext<DataProtectionKeyContext>(options =>
            options.UseSqlServer(connectionString));

        builder.Services
            .AddDataProtection()
            .SetApplicationName(SharedApplicationName)
            .PersistKeysToDbContext<DataProtectionKeyContext>();

        return builder;
    }

    /// <summary>
    /// Creates the key table if it is absent.
    /// <para>
    /// Done with idempotent DDL rather than an EF migration because the table is shared by two
    /// apps and belongs to neither's migration history — whichever starts first creates it.
    /// The shape matches <see cref="DataProtectionKey"/>.
    /// </para>
    /// </summary>
    public static async Task EnsureDataProtectionKeyStoreAsync(
        this IHost host,
        CancellationToken cancellationToken = default)
    {
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DataProtectionExtensions).FullName!);

        var context = services.GetRequiredService<DataProtectionKeyContext>();

        const string ddl = """
            IF OBJECT_ID(N'[dbo].[DataProtectionKeys]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[DataProtectionKeys] (
                    [Id] int NOT NULL IDENTITY,
                    [FriendlyName] nvarchar(max) NULL,
                    [Xml] nvarchar(max) NULL,
                    CONSTRAINT [PK_DataProtectionKeys] PRIMARY KEY ([Id])
                );
            END
            """;

        try
        {
            await context.Database.ExecuteSqlRawAsync(ddl, cancellationToken);
        }
        catch (SqlException exception)
        {
            // A race between the two apps starting together can lose to the other's CREATE
            // even behind the guard. The loser's table already exists, so this is benign.
            logger.LogWarning(
                exception,
                "Could not ensure the Data Protection key table; continuing on the assumption "
                + "another instance created it.");
        }
    }
}
