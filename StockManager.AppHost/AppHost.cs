var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureContainerAppEnvironment("aca-env");

var sql = builder.AddAzureSqlServer("sql")
	.RunAsContainer(container => container
		.WithDataVolume("stockmanager-sql-data")
		.WithLifetime(ContainerLifetime.Persistent));

var identityDatabase = sql.AddDatabase("DefaultConnection", "ApiAuthDb");
var stockDatabase = sql.AddDatabase("SMDatabase", "SMDatabase");
var jwtSigningKey = builder.AddParameter("jwt-signing-key", secret: true);

// Deploy the SMDatabase schema (tables + stored procedures) into the provisioned
// database on every run. Without this, a fresh SQL volume leaves SMDatabase empty
// and calls like dbo.spUserLookup fail with "Could not find stored procedure".
// Points at the DACPAC produced by building SMDatabase.sqlproj in Visual Studio.
var smDacpacPath = Path.GetFullPath(
	Path.Combine(builder.AppHostDirectory, "..", "SMDatabase", "bin", "Debug", "SMDatabase.dacpac"));

var smSchema = builder.AddSqlProject("smdatabase-schema")
	.WithDacpac(smDacpacPath)
	.WithReference(stockDatabase)
	.WaitFor(stockDatabase);

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
