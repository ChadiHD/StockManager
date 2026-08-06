using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureContainerAppEnvironment("aca-env");

var sql = builder.AddAzureSqlServer("sql")
	.RunAsContainer(container => container
		.WithDataVolume("stockmanager-sql-data")
		.WithLifetime(ContainerLifetime.Persistent));

var identityDatabase = sql.AddDatabase("DefaultConnection", "ApiAuthDb");
var stockDatabase = sql.AddDatabase("SMDatabase", "SMDatabase");
var jwtSigningKey = builder.AddParameter("jwt-signing-key", secret: true);

// Distributor feeds are no longer configured here. They are created in the admin portal and
// stored in dbo.DistributorFeed, with credentials held by an IFeedSecretStore (Data Protection
// locally, Key Vault in production) — see FeedSecrets in the API's appsettings.

// Deploy the SMDatabase schema (tables + stored procedures) into the provisioned
// database on every run. Without this, a fresh SQL volume leaves SMDatabase empty
// and calls like dbo.spUserLookup fail with "Could not find stored procedure".
// Points at the DACPAC produced by building SMDatabase.sqlproj in Visual Studio.
var smDacpacPath = Path.GetFullPath(
	Path.Combine(builder.AppHostDirectory, "..", "SMDatabase", "bin", "Debug", "SMDatabase.dacpac"));

// SMDatabase is a classic SSDT project, so the DACPAC is produced by Visual Studio rather than
// by `dotnet build`. Deploying a stale one silently applies old schema and produces confusing
// "invalid object name" / "could not find stored procedure" errors at runtime, so check it here
// and fail with something actionable instead.
if (builder.ExecutionContext.IsRunMode)
{
	var smProjectDirectory = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "SMDatabase"));

	if (!File.Exists(smDacpacPath))
	{
		throw new InvalidOperationException(
			$"SMDatabase.dacpac was not found at '{smDacpacPath}'. Build the SMDatabase project in " +
			"Visual Studio before running the app host.");
	}

	var newestSource = Directory
		.EnumerateFiles(smProjectDirectory, "*.sql", SearchOption.AllDirectories)
		.Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
		            && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
		.Select(File.GetLastWriteTimeUtc)
		.DefaultIfEmpty()
		.Max();

	if (newestSource > File.GetLastWriteTimeUtc(smDacpacPath))
	{
		throw new InvalidOperationException(
			"SMDatabase.dacpac is older than the .sql files in SMDatabase. Rebuild the SMDatabase " +
			"project in Visual Studio, otherwise the app host would deploy out-of-date schema.");
	}
}

var smSchema = builder.AddSqlProject("smdatabase-schema")
	.WithDacpac(smDacpacPath)
	.WithReference(stockDatabase)
	.WaitFor(stockDatabase)
	// The SQL volume is persistent, so skip the redeploy when the dacpac is unchanged.
	// Keeps subsequent app host starts fast instead of republishing every run.
	.WithSkipWhenDeployed();

// CommunityToolkit.Aspire.Hosting.SqlDatabaseProjects tags SQL project resources with
// ExplicitStartupAnnotation ("do not start with the app host"), so smdatabase-schema sits at
// "Not started" until started by hand in the dashboard. Because stock-api below uses
// WaitForCompletion(smSchema), that also stalled the API — and sm-portal / sm-desktop behind
// it. Strip the annotation so the schema deploys automatically on every run.
foreach (var annotation in smSchema.Resource.Annotations.OfType<ExplicitStartupAnnotation>().ToList())
{
	smSchema.Resource.Annotations.Remove(annotation);
}

var api = builder.AddProject<Projects.StockApi>("stock-api")
	.WithReference(identityDatabase)
	.WithReference(stockDatabase)
	.WithEnvironment("Jwt__SigningKey", jwtSigningKey)
	.WaitFor(identityDatabase)
	.WaitFor(stockDatabase)
	.WaitForCompletion(smSchema)
	.WithEndpoint("https", endpoint => endpoint.Port = 7042)
	.WithHttpHealthCheck("/health")
	.WithExternalHttpEndpoints()
	.PublishAsAzureContainerApp((_, _) => { });

builder.AddProject<Projects.SMPortal>("sm-portal")
	.WithReference(api)
	.WaitFor(api)
	.WithExternalHttpEndpoints()
	.PublishAsAzureContainerApp((_, _) => { });

builder.AddProject(
		"sm-desktop",
		"../SMDesktopUI/SMDesktopUI.csproj",
		_ => { })
	.WithReference(api)
	.WaitFor(api);

// Desktop UI automation check (FlaUI). Explicit-start so it runs on demand from the Aspire
// dashboard rather than every launch — it spins up its own WPF window and needs an interactive
// desktop session. Run mode only, so it never becomes part of the published deployment.
if (builder.ExecutionContext.IsRunMode)
{
	builder.AddExecutable(
			"desktop-ui-tests",
			"dotnet",
			workingDirectory: Path.Combine(builder.AppHostDirectory, ".."),
			"test", "SMDesktopUI.UITests/SMDesktopUI.UITests.csproj", "-c", "Debug", "--nologo")
		.WithExplicitStart();
}

builder.Build().Run();
