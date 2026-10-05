using System.Reflection;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SqlServer.Dac;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureContainerAppEnvironment("aca-env");

var sql = builder.AddAzureSqlServer("sql")
	.RunAsContainer(container => container
		.WithDataVolume("stockmanager-sql-data")
		.WithLifetime(ContainerLifetime.Persistent));

var identityDatabase = sql.AddDatabase("DefaultConnection", "ApiAuthDb");
var stockDatabase = sql.AddDatabase("SMDatabase", "SMDatabase");
var jwtSigningKey = builder.AddParameter("jwt-signing-key", secret: true);

// Customer documents: one private blob container both hosts read, since a container app's own
// filesystem is neither shared nor kept across revisions. Azurite locally, on a volume and
// persistent like SQL, because the database rows that name these blobs outlive a restart too.
var documents = builder.AddAzureStorage("storage")
	.RunAsEmulator(emulator => emulator
		.WithDataVolume("stockmanager-documents")
		.WithLifetime(ContainerLifetime.Persistent))
	.AddBlobContainer("documents");

// Distributor feeds are no longer configured here. They are created in the admin portal and
// stored in dbo.DistributorFeed, with credentials held by an IFeedSecretStore (Data Protection
// locally, Key Vault in production) — see FeedSecrets in the API's appsettings.

// Deploy the SMDatabase schema (tables + stored procedures) into the provisioned
// database on every run. Without this, a fresh SQL volume leaves SMDatabase empty
// and calls like dbo.spUserLookup fail with "Could not find stored procedure".
//
// The path is stamped in at build time by the ResolveSMDatabaseDacpacPath target, which asks
// the SQL project itself. It is not derived here because the output location moves with the
// configuration and again with $(BaseOutputPath) — a hardcoded "bin\Debug" pointed at a file
// that a Release or output-redirected build had never written, and the guard below then
// compared against a stale DACPAC or none at all.
var smDacpacPath = Assembly.GetExecutingAssembly()
	.GetCustomAttributes<AssemblyMetadataAttribute>()
	.FirstOrDefault(attribute => attribute.Key == "SMDatabaseDacpacPath")
	?.Value;

if (string.IsNullOrWhiteSpace(smDacpacPath))
{
	throw new InvalidOperationException(
		"The app host was built without the SMDatabaseDacpacPath assembly metadata, so it " +
		"cannot locate the schema to deploy. Check that the SMDatabase.sqlproj ProjectReference " +
		"and the ResolveSMDatabaseDacpacPath target are both still present in " +
		"StockManager.AppHost.csproj, then rebuild.");
}

// Deploying a stale DACPAC silently applies old schema and produces confusing "invalid object
// name" / "could not find stored procedure" errors at runtime, so check it here and fail with
// something actionable instead.
if (builder.ExecutionContext.IsRunMode)
{
	var smProjectDirectory = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "SMDatabase"));

	if (!File.Exists(smDacpacPath))
	{
		throw new InvalidOperationException(
			$"SMDatabase.dacpac was not found at '{smDacpacPath}'. Rebuild the app host before running it.");
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
			"project, otherwise the app host would deploy out-of-date schema.");
	}
}

/*
The schema is published in process, on the database's own ready event, rather than by a
separate resource in the graph.

It used to be a CommunityToolkit AddSqlProject resource that stock-api and sm-store both
WaitForCompletion'd. That resource never ran: it sat in Waiting for ever and took the whole
application down with it, because everything waited on something that would never finish.
Five explanations were checked and none held — the ExplicitStartupAnnotation the toolkit adds
(stripping it changes nothing, and so does leaving it), the resource's own WaitAnnotations
(removing every one of them changes nothing), WithSkipWhenDeployed, the Azure container app
environment, and upgrading Aspire and the toolkit together to 13.5. In every case the
resource reported exactly one Waiting snapshot and never another. Nothing in the app graph
was orchestrating it.

The DACPAC itself was never the problem: published by hand against the same container it
applies in about ten seconds. So this drops the dependency and does the deploy directly,
which also removes a third-party resource type from the startup critical path.

ResourceReadyEvent is the documented seam for exactly this — Aspire awaits its subscribers
before dependents that WaitFor the resource proceed, so stock-api and sm-store need only
WaitFor(stockDatabase) and the schema is guaranteed to be in place before either starts.
That is why WaitForCompletion is gone from both.

What is lost is the dashboard row for the deploy and WithSkipWhenDeployed's fast path. The
row is worth less than an application that starts, and ten seconds per launch is not worth
an optimisation that can leave the schema unapplied.
*/
// Run mode only, and said so rather than left to the event: a published deployment applies the
// schema from the pipeline with BlockOnPossibleDataLoss on (docs/runbooks/production.md), never
// with the setting below.
if (builder.ExecutionContext.IsRunMode)
{
	builder.Eventing.Subscribe<ResourceReadyEvent>(
		stockDatabase.Resource,
		async (readyEvent, cancellationToken) =>
		{
			var logger = readyEvent.Services
				.GetRequiredService<ResourceLoggerService>()
				.GetLogger(stockDatabase.Resource);
	
			var connectionString = await stockDatabase.Resource.ConnectionStringExpression
				.GetValueAsync(cancellationToken)
				?? throw new InvalidOperationException(
					"SMDatabase reported ready without a connection string, so the schema cannot be deployed.");
	
			logger.LogInformation("Publishing {Dacpac} to SMDatabase.", smDacpacPath);
	
			// DacServices is synchronous and the publish takes seconds, so it goes to the thread
			// pool rather than blocking the event handler Aspire is awaiting.
			await Task.Run(() =>
			{
				var dac = new DacServices(connectionString);
	
				dac.Message += (_, message) => logger.LogInformation("{Message}", message.Message.Message);
	
				using var package = DacPackage.Load(smDacpacPath);
	
				dac.Deploy(
					package,
					targetDatabaseName: "SMDatabase",
					upgradeExisting: true,
					options: new DacDeployOptions
					{
						/*
						Local development only, and it has to stay that way.
	
						SqlPackage refuses any change it classifies as possibly lossy while a table
						has rows, and "possibly" is doing a lot of work: tightening SiteId from NULL
						to NOT NULL rebuilds the Account table — copy, drop, rename — which trips the
						guard even though no column is being dropped and no type narrowed. On a
						persistent development volume that is a hard stop.
	
						The protection this gives up is real. It is what would otherwise catch a
						column being dropped or a type narrowed by accident, and the only reason it
						is acceptable here is that this database is a local container backed by a
						volume anyone can delete and reseed.
	
						T7 owns the production deployment path. It must not inherit this setting. A
						real database takes the review and the backup instead.
						*/
						BlockOnPossibleDataLoss = false
					},
					cancellationToken: cancellationToken);
			}, cancellationToken);
	
			logger.LogInformation("SMDatabase schema is up to date.");
		});
}

var api = builder.AddProject<Projects.StockApi>("stock-api")
	.WithReference(identityDatabase)
	.WithReference(stockDatabase)
	.WithReference(documents)
	.WaitFor(identityDatabase)
	.WaitFor(stockDatabase)
	.WaitFor(documents)
	.WithEndpoint("https", endpoint => endpoint.Port = 7042)
	.WithHttpHealthCheck("/health")
	.WithExternalHttpEndpoints();

// No sm-portal resource: StockApi serves the admin portal's WebAssembly bundle itself (T7),
// so the portal and the API are one origin and one container.

// Customer-facing storefront. Unlike sm-portal it holds no reference to the API: it renders on
// the server and reads SMDatabase through SMDataManager.Library in process. One deployment
// serves every store, resolving the site from the request host, so this stays a single
// resource however many sites exist.
var store = builder.AddProject<Projects.SMStore>("sm-store")
	.WithReference(stockDatabase)
	// ApiAuthDb, for two things: the Data Protection key ring it shares with the API, and the
	// Identity user store it creates customer logins in. It reads and writes that store but
	// never migrates it — StockApi owns the migrations, and both start together.
	.WithReference(identityDatabase)
	.WithReference(documents)
	.WaitFor(stockDatabase)
	.WaitFor(identityDatabase)
	.WaitFor(documents)
	.WithHttpHealthCheck("/health")
	.WithExternalHttpEndpoints();

if (builder.ExecutionContext.IsRunMode)
{
	// Locally, the signing key is a user-secret parameter. Published, it is a Key Vault secret;
	// see ProductionTopology.
	api.WithEnvironment("Jwt__SigningKey", jwtSigningKey);
}
else
{
	ProductionTopology.Apply(builder, api, store, jwtSigningKey);
}

// The WPF POS and its UI tests are Windows desktop programs: run mode on Windows only. Published,
// a container platform would try to build them into images; on a Linux CI runner the app host
// would try to start a WPF window.
if (builder.ExecutionContext.IsRunMode && OperatingSystem.IsWindows())
{
	builder.AddProject(
			"sm-desktop",
			"../SMDesktopUI/SMDesktopUI.csproj",
			_ => { })
		.WithReference(api)
		.WaitFor(api);

	// Desktop UI automation check (FlaUI). Explicit-start so it runs on demand from the Aspire
	// dashboard rather than every launch — it spins up its own WPF window and needs an
	// interactive desktop session.
	builder.AddExecutable(
			"desktop-ui-tests",
			"dotnet",
			workingDirectory: Path.Combine(builder.AppHostDirectory, ".."),
			"test", "SMDesktopUI.UITests/SMDesktopUI.UITests.csproj", "-c", "Debug", "--nologo")
		.WithExplicitStart();
}

builder.Build().Run();
