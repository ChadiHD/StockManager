using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Pricing;
using SMStore.Accounts;
using SMStore.Components;
using SMStore.Catalog;
using SMStore.Content;
using SMStore.Documents;
using SMStore.Navigation;
using SMStore.Ordering;
using SMStore.Registration;
using StockManager.Documents;
using StockManager.Identity;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SMStore.Sites;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMemoryCache();

/*
The upload cap, enforced before the bytes are accepted rather than after.

DocumentUploadService checks each file against the same number and says something useful
about it, but a check in code runs only once the body has already been read — which is no
protection at all against someone posting a gigabyte. So the limit is set here too, and it is
the one that actually refuses.

Sized for the whole form rather than one file: an application carries several documents plus
its text fields, so the request limit is a multiple of the per-file cap. A file over the
per-file cap but under this one is what reaches the friendly message; anything over this gets
a 413 and deserves it.

Registration is the only upload on the storefront, so a global limit is the right shape. If a
second one ever appears, this becomes per-endpoint metadata instead.
*/
var maxDocumentBytes =
    builder.Configuration.GetValue<long?>($"{DocumentStoreOptions.SectionName}:MaxBytes")
    ?? new DocumentStoreOptions().MaxBytes;

var maxRequestBytes = (maxDocumentBytes * 4) + (1024 * 1024);

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxRequestBytes);

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxRequestBytes;
    options.MultipartHeadersLengthLimit = 16 * 1024;
});

// Same key ring as StockApi, held in the identity database, so a cookie issued by either host
// is readable by both. That is what makes storefront sign-in cheap to add, and also why a
// valid cookie proves only who someone is: which store they may use is checked separately,
// through Contact -> Account -> Site.
builder.AddSharedDataProtection();

// Customer-uploaded documents. The bytes live outside the web root; the only way to one is
// an endpoint that re-checks who is asking. StockApi registers the same store so a reviewer
// can read what an applicant sent.
builder.AddDocumentStore();

// Outbound customer mail. A logger until T6 supplies a transport — see AddEmail.
builder.AddEmail();

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

// The basket. BasketService reads the request cookie, so it needs the accessor and has to be
// scoped; nothing else in the storefront resolves HttpContext outside an endpoint.
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<IBasketData, BasketData>();
builder.Services.AddScoped<BasketService>();
builder.Services.AddScoped<BasketPresenter>();

// What a customer application demands, likewise per site. Field sets are stateless rules, so
// singletons; the provider is scoped because it reads the request's site.
builder.Services.AddSingleton<IRegistrationFieldSet, EuB2bRegistrationFieldSet>();
builder.Services.AddScoped<RegistrationFieldSetProvider>();

/*
Identity, for creating and later authenticating customer logins.

The same ApplicationDbContext StockApi uses, against the same ApiAuthDb — hence the shared
StockManager.Identity project. **This host must never migrate it.** StockApi calls
Database.Migrate() at startup and the app host starts both together; two processes applying
migrations to one database race on the history table.

AddIdentityCore rather than AddDefaultIdentity: this wants UserManager and the token
providers, and nothing else. AddDefaultIdentity would bring the Identity UI's Razor pages
with their own /Identity/Account/Login — a second sign-in form on the storefront that knows
nothing about which store it is serving, and would happily authenticate another store's
customer. The cookie scheme below is this storefront's own.
*/
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentityCore<IdentityUser>(options =>
    {
        // Both hosts must agree, because they share one user store. See SiteQualifiedUserName.
        options.User.AllowedUserNameCharacters = SiteQualifiedUserName.AllowedUserNameCharacters;

        // One person may hold an account at several of the stores run from here, so the
        // address is not unique — the username carries the store and is.
        options.User.RequireUniqueEmail = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddScoped<IAccountRegistrationData, AccountRegistrationData>();
builder.Services.AddScoped<IRegistrationService, RegistrationService>();

// Password reset: mailing a link and spending it. Scoped because it reads the request's site.
builder.Services.AddScoped<PasswordResetService>();
builder.Services.AddTransient<IContactData, ContactData>();
builder.Services.AddTransient<IAddressData, AddressData>();

// Read-only here. Every method on IAccountData takes a siteId and the account area passes
// the resolved site's, but the write methods on it are an administrator's — the storefront
// has no screen that calls one, and adding one would need the same thought about who may.
builder.Services.AddTransient<IAccountData, AccountData>();
builder.Services.AddTransient<IAccountDocumentData, AccountDocumentData>();
builder.Services.AddScoped<DocumentUploadService>();

/*
Customer sign-in: a cookie scheme of this storefront's own.

Not IdentityConstants.ApplicationScheme, and not the Identity cookie name. StockApi uses
those for the Razor admin UI, the two hosts share a hostname in development, and cookies
ignore ports — so identical names would have the two sessions overwriting each other, and the
shared key ring means each can read what the other wrote.

OnValidatePrincipal is where the session is actually checked. See CustomerSessionValidator:
a cookie proves who someone is, and which store they may use is a different question that
only the database can answer.
*/
builder.Services.AddScoped<CustomerContext>();
builder.Services.AddScoped<ICustomerContext>(services => services.GetRequiredService<CustomerContext>());

builder.Services.AddAuthentication(CustomerAuthentication.Scheme)
    .AddCookie(CustomerAuthentication.Scheme, options =>
    {
        options.Cookie.Name = CustomerAuthentication.CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // Always, not SameAsRequest: the storefront redirects to HTTPS anyway, and a cookie
        // that would travel in clear over a misconfigured hop is worth refusing to send.
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

        options.LoginPath = CustomerAuthentication.LoginPath;
        options.LogoutPath = CustomerAuthentication.LogoutPath;
        options.AccessDeniedPath = CustomerAuthentication.AccessDeniedPath;

        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);

        options.Events.OnValidatePrincipal = CustomerSessionValidator.ValidateAsync;
    });

builder.Services.AddAuthorization();

// Caps registration and sign-in per caller. Everything else is unlimited -- see
// CustomerRateLimiting for why a global cap on the catalog would be the wrong shape.
builder.Services.AddCustomerRateLimiting();

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

// After site resolution, and that ordering is load-bearing: CustomerSessionValidator runs
// inside the cookie handler and asks ISiteContext which store this request is for. Put
// authentication first and it would validate every session against an unresolved site.
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

// After antiforgery so a rejected forgery is not also charged to the caller's rate limit,
// and before the endpoints it protects.
app.UseRateLimiter();

app.MapStaticAssets();
app.MapCustomerAuth();
app.MapBasket();
app.MapAccountDocuments();
app.MapEmailConfirmation();
app.MapPasswordReset();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
