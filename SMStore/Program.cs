using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Pricing;
using SMStore.Components;
using SMStore.Catalog;
using SMStore.Content;
using SMStore.Navigation;
using SMStore.Ordering;
using SMStore.Registration;
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
builder.Services.AddTransient<ICatalogData, CatalogData>();

// One price rule, shared with admin quote pricing. Two implementations would drift, and the
// first anyone would hear of it is a customer quoted one price on the catalog and another on
// their quote.
builder.Services.AddSingleton<IPriceResolver, PriceResolver>();
builder.Services.AddScoped<CatalogPresenter>();

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

// What a customer application demands, likewise per site. Field sets are stateless rules, so
// singletons; the provider is scoped because it reads the request's site.
builder.Services.AddSingleton<IRegistrationFieldSet, EuB2bRegistrationFieldSet>();
builder.Services.AddScoped<RegistrationFieldSetProvider>();

// Navigation is assembled rather than written into markup, so a site can vary it and the
// basket entry can follow the ordering mode.
builder.Services.AddScoped<StoreNavigation>();

// Editorial content is per store, read from dbo.SiteContent. A store that has published
// nothing under a key still renders an explicit empty state rather than borrowed words.
builder.Services.AddTransient<ISiteContentData, SiteContentData>();
builder.Services.AddScoped<ISiteContentSource, DatabaseSiteContentSource>();

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
