using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace StockManager.Identity
{
    /// <summary>
    /// ASP.NET Identity's own schema, in ApiAuthDb. The only EF context in the solution —
    /// business data goes through Dapper and stored procedures.
    /// </summary>
    /// <remarks>
    /// This lives in a project of its own because two hosts need it and neither can reference
    /// the other. StockApi authenticates staff and admins; SMStore creates and authenticates
    /// customer logins. Duplicating an empty context in each was the alternative, and two
    /// classes that must stay identical is how they stop being identical.
    ///
    /// **Only StockApi migrates it.** Its Program.cs calls Database.Migrate() at startup;
    /// SMStore must never do the same. Two hosts racing to apply migrations to one database
    /// corrupts the history table, and the app host starts them together. The migrations
    /// themselves stay in StockApi — see the MigrationsAssembly call in its AddDbContext — so
    /// the dotnet-ef workflow is unchanged.
    /// </remarks>
    public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
        }
    }
}
