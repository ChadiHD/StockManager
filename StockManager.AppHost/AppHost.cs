var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("sql")
	.WithDataVolume("stockmanager-sql-data")
	.WithLifetime(ContainerLifetime.Persistent);

var identityDatabase = sql.AddDatabase("DefaultConnection", "ApiAuthDb");
var stockDatabase = sql.AddDatabase("SMDatabase", "SMDatabase");

var api = builder.AddProject<Projects.StockApi>("stock-api")
	.WithReference(identityDatabase)
	.WithReference(stockDatabase)
	.WaitFor(identityDatabase)
	.WaitFor(stockDatabase)
	.WithEndpoint("https", endpoint => endpoint.Port = 7042)
	.WithHttpHealthCheck("/health")
	.WithExternalHttpEndpoints();

builder.AddProject<Projects.SMPortal>("sm-portal")
	.WithReference(api)
	.WaitFor(api)
	.WithExternalHttpEndpoints();

builder.AddProject(
		"sm-desktop",
		"../SMDesktopUI/SMDesktopUI.csproj",
		_ => { })
	.WithReference(api)
	.WaitFor(api);

builder.Build().Run();
