using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Internal.DataAccess;
using SMStore.Components;
using SMStore.Content;
using SMStore.Navigation;
using SMStore.Ordering;
using SMStore.Sites;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMemoryCache();

// Same key ring as StockApi, held in the identity database. Shared from the start so that
// when sign-in lands in T3 a cookie issued by either host is readable by both.
builder.AddSharedDataProtection();

// Data access. The storefront reads the same stored procedures the API does, in process —
// see the note on the project reference in SMStore.csproj.
builder.Services.AddTransient<ISqlDataAccess, SqlDataAccess>();
builder.Services.AddTransient<ISiteData, SiteData>();

// Multi-store plumbing. SiteContext is registered as itself and behind the interface so
// middleware can write to it while everything else only reads.
builder.Services.Configure<SiteOptions>(builder.Configuration.GetSection(SiteOptions.SectionName));
builder.Services.AddSingleton<SiteResolver>();
builder.Services.AddSingleton<SiteThemeResolver>();
builder.Services.AddScoped<SiteContext>();
builder.Services.AddScoped<ISiteContext>(services => services.GetRequiredService<SiteContext>());

// Ordering behaviour is per site. Register every implementation; OrderingModeProvider picks.
builder.Services.AddSingleton<IOrderingMode, RfqOrderingMode>();
builder.Services.AddScoped<OrderingModeProvider>();

// Navigation is assembled rather than written into markup, so a site can vary it and the
// basket entry can follow the ordering mode.
builder.Services.AddScoped<StoreNavigation>();

// Editorial content is per store. The null source serves nothing, so content pages render an
// explicit empty state until a real source is registered in its place.
builder.Services.AddScoped<ISiteContentSource, NullSiteContentSource>();

var app = builder.Build();

await app.EnsureDataProtectionKeyStoreAsync();

// ForceSiteKey pins every request to one store regardless of host. That is a development
// affordance; in production it would serve one tenant's pages on every domain, so refuse to
// start rather than let it slip through a config file.
if (!app.Environment.IsDevelopment()
    && !string.IsNullOrWhiteSpace(app.Configuration[$"{SiteOptions.SectionName}:ForceSiteKey"]))
{
    throw new InvalidOperationException(
        "Sites:ForceSiteKey is set outside Development. It bypasses host-based site resolution " +
        "and would serve a single store on every domain.");
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// Health probes are addressed by container host or IP, which no Site row claims, so they must
// not go through host-based resolution or Aspire would see every instance as unhealthy.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/health")
            && !context.Request.Path.StartsWithSegments("/alive"),
    branch => branch.UseSiteResolution());

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
