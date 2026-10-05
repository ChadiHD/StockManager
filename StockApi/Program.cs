using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Email;
using SMDataManager.Library.Feeds;
using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using SMDataManager.Library.Tax;
using StockApi.Email;
using StockApi.Feeds;
using StockApi.Quotes;
using StockApi.Scheduling;
using StockApi.Security;
using StockApi.Sites;
using StockManager.Identity;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
// ApplicationDbContext now lives in StockManager.Identity, shared with SMStore, but its
// migrations stayed here — EF looks for them in the context's own assembly unless told
// otherwise, and moving generated files to keep a default happy is a poor trade. StockApi
// remains the only host that migrates; see the remarks on ApplicationDbContext.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.MigrationsAssembly("StockApi")));

/*
AddIdentityCore, not AddDefaultIdentity: this host needs the user and role managers and a
password check that counts failures, and nothing else.

AddDefaultIdentity brought the Identity UI's Razor pages — /Identity/Account/Register, Login,
ForgotPassword — onto the API's own domain, and nothing used them. Their Login signs in with
lockoutOnFailure: false, and a customer login name ({SiteKey}|{email}) passes its
[EmailAddress] check, so it was an unlimited password oracle for every store's customers that
answered with a cookie this host accepted.
*/
builder.Services.AddIdentityCore<IdentityUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;

        // AddDefaultIdentity set this, and the Identity migrations were generated under it:
        // without it the model no longer matches them and Migrate() refuses to start.
        options.Stores.MaxLengthForKeys = 128;

        // Both hosts share one user store; see SiteQualifiedUserName.
        options.User.AllowedUserNameCharacters = SiteQualifiedUserName.AllowedUserNameCharacters;
        options.User.RequireUniqueEmail = false;

        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager();

// Controllers only. Views, Razor pages and the MVC home page were the project template's, and
// an API host serving HTML is surface nobody reviews.
builder.Services.AddControllers();
builder.Services.AddProblemDetails();

// /token is the staff sign-in, and the only password check on this host.
builder.Services.AddStaffRateLimiting();

builder.Services.AddTransient<IInventoryData, InventoryData>();
builder.Services.AddTransient<ISqlDataAccess, SqlDataAccess>();
builder.Services.AddTransient<IProductData, ProductData>();
builder.Services.AddTransient<ICategoryMappingData, CategoryMappingData>();
builder.Services.AddTransient<IPurchaseData, PurchaseData>();
builder.Services.AddTransient<IUserData, UserData>();

// Same rule set the storefront uses: an admin converting a quote and a customer accepting
// one must not reach different answers about the same sale.
builder.Services.AddSingleton<ITaxRuleSet, EuB2bTaxRuleSet>();
builder.Services.AddSingleton<TaxRuleSetProvider>();
builder.Services.AddSingleton<TaxAssessor>();
// Distributor stock feeds are defined in the database and managed from the admin portal.
// Credentials never live in appsettings or in the feed table — an IFeedSecretStore holds them
// and the row keeps only a reference. FeedSecrets:Provider selects the store per environment.
builder.Services.AddTransient<IDistributorFeedClient, SftpDistributorFeedClient>();
builder.Services.AddTransient<IDistributorFeedSyncService, DistributorFeedSyncService>();
builder.Services.AddTransient<IDistributorFeedData, DistributorFeedData>();

// Icecat product-content lookups, used to fill in missing product images. Inert until an
// Icecat account is configured; the client reports IsConfigured = false and callers no-op.
builder.Services.AddHttpClient<IIcecatClient, IcecatClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddTransient<IBrandAliasData, BrandAliasData>();

// Singleton so the alias cache survives between enrichment batches; a transient would re-read
// dbo.BrandAlias once per product. A singleton's factory closes over the *root* provider, so it
// scopes each refresh explicitly — resolving the transient IBrandAliasData straight from the
// root would leave its SqlDataAccess on the root's disposables list, one per refresh, for the
// life of the process.
builder.Services.AddSingleton<IBrandAliasResolver>(services =>
{
    var scopes = services.GetRequiredService<IServiceScopeFactory>();

    return new BrandAliasResolver(() =>
    {
        using var scope = scopes.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IBrandAliasData>().GetAliases();
    });
});

builder.Services.AddTransient<IProductImageEnricher, ProductImageEnricher>();

// Singleton so a finished sync and the background worker share the same signal — the sync
// asks for a pass, the worker is waiting on it. Two instances would mean the request is
// raised on one and awaited on the other, and nothing would ever wake.
builder.Services.AddSingleton<IImageEnrichmentSignal, ImageEnrichmentSignal>();
builder.Services.AddHostedService<ProductImageBackgroundService>();

// The nightly distributor feed sync, off unless Feeds:SyncEnabled says otherwise. Here rather
// than in SMStore because this host holds the Data Protection ring that decrypts a feed's
// stored credential; see DistributorFeedSyncBackgroundService.
builder.Services.AddHostedService<DistributorFeedSyncBackgroundService>();

// Tells a store's operator when a feed starts failing or its data goes stale. Scoped because
// it reads per-site data; resolved by the scheduler and by nothing else.
builder.Services.AddScoped<FeedAlertService>();

// Data Protection keys live in the identity database, shared with SMStore. This replaces the
// default per-container filesystem ring, which lost saved feed credentials on every redeploy
// and could not be read by a second app. See AddSharedDataProtection.
builder.AddSharedDataProtection();

// Customer-uploaded documents, written by SMStore and read back here by whoever reviews the
// application. Both hosts must resolve the same container root, or a reviewer opens an empty
// store — see AddDocumentStore.
builder.AddDocumentStore();

// Outbound mail. The transport is still the logger — see AddEmail — and since T6 nothing
// calls it but the dispatcher: call sites queue through IEmailOutbox, procedures queue inside
// their own transactions, and EmailDispatcher renders and sends with retry and dead-letter.
// A real transport when one is configured, registered before AddEmail so its TryAdd of the
// logger finds the sender already there. Development leaves it unset and logs.
if (string.Equals(builder.Configuration["Email:Transport"], "Acs", StringComparison.OrdinalIgnoreCase))
{
    var acsEndpoint = builder.Configuration["Email:AcsEndpoint"]
        ?? throw new InvalidOperationException("Email:Transport is Acs but Email:AcsEndpoint is not set.");

    builder.Services.AddSingleton(new Azure.Communication.Email.EmailClient(
        new Uri(acsEndpoint), new Azure.Identity.DefaultAzureCredential()));
    builder.Services.AddSingleton<StockManager.Notifications.IEmailSender, AcsEmailSender>();
}

builder.AddEmail();
builder.Services.AddTransient<IEmailOutboxData, EmailOutboxData>();
builder.Services.AddSingleton<OutboxPayloadProtector>();
builder.Services.AddScoped<IEmailOutbox, EmailOutbox>();
builder.Services.AddTransient<ISiteEmailTemplateData, SiteEmailTemplateData>();
builder.Services.AddScoped<EmailDispatcher>();

// Here and not in SMStore: one dispatcher is all the volume needs. On by default, unlike the
// feed sync, for the reason EmailDispatchBackgroundService gives.
builder.Services.AddHostedService<EmailDispatchBackgroundService>();

// "Your quote expires soon", once a day, off unless Quotes:ExpiryNoticeEnabled says otherwise —
// the one job here that writes to customers without anybody having done anything. Beside the
// feed sync, and moving with it if T7 decides scheduled work belongs elsewhere.
builder.Services.AddHostedService<QuoteExpiryBackgroundService>();

// Abandoned anonymous baskets and old sent mail, once a day, on by default; see the service.
builder.Services.AddTransient<IHousekeepingData, HousekeepingData>();
builder.Services.AddHostedService<HousekeepingBackgroundService>();

builder.Services.AddSingleton<IFeedSecretStore, DataProtectionFeedSecretStore>();

// Key Vault store, registered only when configured so local development needs no Azure.
if (!string.IsNullOrWhiteSpace(builder.Configuration["FeedSecrets:KeyVaultUri"]))
{
    builder.Services.AddSingleton<IFeedSecretStore, KeyVaultFeedSecretStore>();
}

builder.Services.AddSingleton<IFeedSecretStoreResolver, FeedSecretStoreResolver>();

// Which store a request is acting for. Scoped, written once by the middleware, read-only to
// everything else — the same split SMStore uses for its host-resolved site.
builder.Services.AddMemoryCache();
builder.Services.AddTransient<ISiteData, SiteData>();
builder.Services.AddScoped<AdminSiteContext>();
builder.Services.AddScoped<IAdminSiteContext>(services => services.GetRequiredService<AdminSiteContext>());

builder.Services.AddTransient<IAccountData, AccountData>();
// Children of Account. None carries a site of its own; every procedure behind these joins
// Account for the predicate, so a guessed id resolves to nothing rather than to another
// store's customer records.
builder.Services.AddTransient<IContactData, ContactData>();
builder.Services.AddTransient<IAddressData, AddressData>();
builder.Services.AddTransient<IAccountDocumentData, AccountDocumentData>();
builder.Services.AddTransient<ICustomerGroupData, CustomerGroupData>();
builder.Services.AddTransient<IQuoteData, QuoteData>();
builder.Services.AddTransient<IOrderData, OrderData>();

var jwtSigningKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException("Missing configuration value: Jwt:SigningKey");
var jwtSigningKeyBytes = Encoding.UTF8.GetBytes(jwtSigningKey);

// Bearer tokens only. There was a policy scheme here routing cookie-carrying requests to the
// Identity cookie, for the Razor UI that is gone; with it, a cookie from that UI's Login
// authenticated API calls.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
.AddJwtBearer(jwtBearerOptions =>
{
    jwtBearerOptions.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(jwtSigningKeyBytes),
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(5)
    };
});

builder.Services.AddAuthorization();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "StockManager API", Version = "v1" });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
	var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
	db.Database.Migrate();
}

await app.EnsureDataProtectionKeyStoreAsync();

// The staff roles, and on a fresh deployment its first admin; see AdminBootstrap.
await AdminBootstrap.EnsureAdminAsync(app.Services, app.Configuration, app.Logger);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler();
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

// For the portal, since this host serves its pages: WebAssembly needs 'wasm-unsafe-eval', and
// the portal's markup sets style attributes. Swagger's UI is inline script and Development-only,
// so it is left out rather than loosening the policy for everything else.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/swagger"),
    branch => branch.UseSecurityHeaders(
        "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: https:; font-src 'self'; connect-src 'self'; form-action 'self'; " +
        "frame-ancestors 'none'; base-uri 'self'; object-src 'none'"));

// The admin portal's WebAssembly bundle, from the SMPortal project reference. Served here
// rather than from a host of its own, so the portal and the API are one origin: no CORS
// policy to get wrong, and no API address to configure per environment — the portal calls
// its own base address. There was an allow-any-origin policy before this.
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// After routing, so the endpoint's [EnableRateLimiting] is visible to it.
app.UseRateLimiter();

// After authorization: resolving a store is only meaningful for a caller that got this far,
// and an anonymous request has no business learning whether a given site key exists.
//
// For api/ only. Without a header it reads the site list, so on the health probes it made
// liveness depend on the database — a database blip would have had the platform restart every
// healthy replica — and on the portal's own pages it was a query to serve a static file.
app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseMiddleware<AdminSiteResolutionMiddleware>());

// A map of every endpoint and its parameters is a development tool, not something to publish.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "StockManager API v1");
    });
}

app.MapControllers();
app.MapDefaultEndpoints();

// The portal's own routes load its page; nothing else does. A catch-all would answer a
// mistyped api/ path with the portal's HTML and a 200, which is a confusing thing for the
// POS or a script to get back instead of a 404.
app.MapFallbackToFile("", "index.html");
app.MapFallbackToFile("login", "index.html");
app.MapFallbackToFile("logout", "index.html");
app.MapFallbackToFile("admin/{*path:nonfile}", "index.html");

app.Run();
