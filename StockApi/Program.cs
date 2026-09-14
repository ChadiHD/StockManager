using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Feeds;
using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Feeds;
using StockApi.Security;
using StockApi.Sites;
using StockApi.Data;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddCors(policy =>
{
    policy.AddPolicy("OpenCorsPolicy", opt =>
        opt.AllowAnyOrigin()
        .AllowAnyHeader()
        .AllowAnyMethod());
});
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

builder.Services.AddTransient<IInventoryData, InventoryData>();
builder.Services.AddTransient<ISqlDataAccess, SqlDataAccess>();
builder.Services.AddTransient<IProductData, ProductData>();
builder.Services.AddTransient<IPurchaseData, PurchaseData>();
builder.Services.AddTransient<IUserData, UserData>();
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

// Data Protection keys live in the identity database, shared with SMStore. This replaces the
// default per-container filesystem ring, which lost saved feed credentials on every redeploy
// and could not be read by a second app. See AddSharedDataProtection.
builder.AddSharedDataProtection();

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

builder.Services.AddAuthentication(options =>
{
    // The interactive Razor UI signs in with the Identity application cookie, while API
    // clients (desktop / Blazor) send a JWT bearer token. Selecting one scheme as the global
    // default breaks the other, so route per-request via a policy scheme (see below).
    options.DefaultScheme = "SmartScheme";
    options.DefaultChallengeScheme = "SmartScheme";
})
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
})
// Null display name keeps this internal routing scheme out of the Identity UI's
// external-login provider list.
.AddPolicyScheme("SmartScheme", displayName: null, options =>
{
    options.ForwardDefaultSelector = context =>
    {
        string authorization = context.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(authorization) &&
            authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return JwtBearerDefaults.AuthenticationScheme;
        }

        // No bearer token -> browser request -> use the Identity cookie so the Razor UI
        // stays signed in after login.
        return IdentityConstants.ApplicationScheme;
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

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseCors("OpenCorsPolicy");
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// After authorization: resolving a store is only meaningful for a caller that got this far,
// and an anonymous request has no business learning whether a given site key exists.
app.UseMiddleware<AdminSiteResolutionMiddleware>();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "StockManager API v1");
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();
app.MapDefaultEndpoints();

app.Run();
