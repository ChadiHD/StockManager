var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureContainerAppEnvironment("aca-env");

var sql = builder.AddAzureSqlServer("sql")
	.RunAsContainer(container => container
		.WithDataVolume("stockmanager-sql-data")
		.WithLifetime(ContainerLifetime.Persistent));

var identityDatabase = sql.AddDatabase("DefaultConnection", "ApiAuthDb");
var stockDatabase = sql.AddDatabase("SMDatabase", "SMDatabase");

var api = builder.AddProject<Projects.StockApi>("stock-api")
	.WithReference(identityDatabase)
	.WithReference(stockDatabase)
	.WaitFor(identityDatabase)
	.WaitFor(stockDatabase)
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

builder.Build().Run();
