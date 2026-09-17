using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Playwright;
using Xunit;

namespace StockManager.E2ETests.Infrastructure;

/// <summary>
/// Starts the whole Aspire application graph once for the assembly and hands every journey
/// test the endpoints, connection strings and browser it needs.
/// </summary>
/// <remarks>
/// Starting the app host per test would mean provisioning the SQL Server container and
/// publishing the SMDatabase DACPAC (slow on a clean volume -- see CLAUDE.md) once per test
/// rather than once per run, so this is an <see cref="ICollectionFixture{T}"/> shared by every
/// class in <see cref="E2ECollection"/>.
///
/// Neither a container runtime nor an installed Playwright browser can be assumed present on
/// whatever machine runs `dotnet test`, so nothing in here throws when either is missing.
/// <see cref="InitializeAsync"/> records why instead, and every journey starts by skipping
/// itself with that reason (see the SkippableFact/Skip.If calls in the Journeys folder)
/// rather than the whole collection failing with a fixture-initialization error.
/// </remarks>
public sealed class AspireAppFixture : IAsyncLifetime
{
    // First run on an empty volume pulls the SQL Server image and publishes the whole
    // SMDatabase DACPAC, which comfortably exceeds Aspire's own default health-check timeout.
    // Overridable for a slower machine or a warmer cache without editing the test.
    private static readonly TimeSpan ResourceHealthyTimeout = TimeSpan.FromMinutes(
        int.TryParse(Environment.GetEnvironmentVariable("E2E_RESOURCE_TIMEOUT_MINUTES"), out var minutes)
            ? minutes
            : 10);

    /// <summary>
    /// Set in CI. Turns "the app host would not start" from a skip into a failure.
    /// </summary>
    /// <remarks>
    /// A missing container runtime stays a skip either way — that is a prerequisite, not a
    /// regression, and it is checked before anything is started.
    /// </remarks>
    private static bool RequireAppHost =>
        Environment.GetEnvironmentVariable("E2E_REQUIRE_APPHOST") is "1" or "true";

    private DistributedApplication? _app;
    private IPlaywright? _playwright;

    /// <summary>Null once the app host started and became healthy; otherwise why every test
    /// in the run must skip.</summary>
    public string? StartupFailure { get; private set; }

    /// <summary>Null once a browser launched; otherwise why every test in the run must skip.</summary>
    public string? BrowserUnavailable { get; private set; }

    /// <summary>
    /// Pinned rather than resolved through <c>GetEndpoint</c>: CLAUDE.md is explicit that
    /// StockApi is pinned to 7042 because the Blazor and WPF clients hardcode it, so testing
    /// it dynamically would be testing a guarantee this codebase does not rely on anywhere
    /// else.
    /// </summary>
    public Uri StockApiBaseUrl { get; } = new("https://localhost:7042");

    public Uri SmStoreBaseUrl { get; private set; } = null!;
    public Uri SmPortalBaseUrl { get; private set; } = null!;

    public string SmDatabaseConnectionString { get; private set; } = "";
    public string ApiAuthConnectionString { get; private set; } = "";

    public IBrowser Browser { get; private set; } = null!;

    /// <summary>The one operator every admin-facing journey signs in as.</summary>
    public IdentityTestSupport.Credentials Admin { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (!await ContainerRuntimeProbe.IsAnyRuntimeAvailableAsync())
        {
            StartupFailure =
                "Neither `docker version` nor `podman version` answered. These tests provision " +
                "SQL Server as a container (StockManager.AppHost/AppHost.cs) -- start Podman " +
                "Desktop (or Docker) and try again.";
            return;
        }

        try
        {
            /*
            The app host's user secrets are loaded explicitly, and without them nothing here
            works.

            Aspire generates the SQL container's SA password once and persists it to the app
            host's user secrets. That container is persistent — a named volume and
            ContainerLifetime.Persistent — so the first password generated is the one baked
            into it for good. `dotnet run` reloads it from user secrets and matches.
            DistributedApplicationTestingBuilder does not load them, so it generated a fresh
            random password every run and then could not authenticate against the container
            that already existed.

            The symptom was nothing like the cause: sql and SMDatabase reached Running and
            then flipped Healthy -> Unhealthy, and stock-api, sm-store and sm-portal all
            reached FailedToStart because they could not connect either.

            This project carries the app host's UserSecretsId so the two share one store
            rather than one guessing at the other's password. See its csproj.
            */
            var appHostBuilder = await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.StockManager_AppHost>(
                    // As a command-line argument, not through the configuration callback
                    // below: DistributedApplicationTestingBuilder sets RandomizePorts itself
                    // after that callback runs, so a value set there is overwritten and the
                    // ports come out random anyway. Command-line configuration outranks it.
                    ["--DcpPublisher:RandomizePorts=false"],
                    (_, settings) =>
                    {
                        settings.Configuration?.AddUserSecrets<AspireAppFixture>(optional: true);

                    });

            // sm-desktop is a real WPF window with no bearing on any of the four journeys here.
            // Nothing stops it auto-starting, so mark it explicit-start: `dotnet test` should not
            // pop a window on whatever desktop happens to run it.
            var desktop = appHostBuilder.Resources.FirstOrDefault(resource => resource.Name == "sm-desktop");
            desktop?.Annotations.Add(new ExplicitStartupAnnotation());

            _app = await appHostBuilder.BuildAsync();


            /*
            The timeout covers StartAsync as well, and that is not tidiness.

            It used to be created after StartAsync, so a start that never returned was unbounded.
            That happened repeatedly while the schema deployment was broken: runs sat for over half
            an hour with E2E_REQUIRE_APPHOST=1 set and a resource timeout configured, because neither
            applied to the call that was actually stuck.

            A test suite is allowed to fail. It is not allowed to hang, because a hang is
            indistinguishable from slow work and cannot be acted on.
            */
            using var timeout = new CancellationTokenSource(ResourceHealthyTimeout);

            try
            {
                await _app.StartAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                /*
                Rewritten because the raw exception is "A task was canceled" with a stack in
                Aspire's own factory, which says nothing about which resource is stuck.

                The first place to look is the SMDatabase schema deployment in AppHost.cs.
                It runs on the database's ResourceReadyEvent and every app waits on that
                database, so a deployment that throws or never returns holds up the whole
                application — which is how this suite spent weeks unable to start anything.
                */
                throw new TimeoutException(
                    $"The app host did not finish starting within {ResourceHealthyTimeout.TotalMinutes} " +
                    "minutes. Look first at the SMDatabase schema deployment in AppHost.cs: it runs " +
                    "on the database's ready event and every app waits on that database, so a " +
                    "deployment that throws or never returns publishes no endpoint at all. Raise " +
                    "E2E_RESOURCE_TIMEOUT_MINUTES only once you have ruled that out.");
            }

            // stock-api and sm-store both carry WithHttpHealthCheck; sm-portal is a static
            // WASM host with no health endpoint of its own, so Running -- not Healthy, which
            // it would never reach -- is the corresponding signal for it.
            await _app.ResourceNotifications.WaitForResourceHealthyAsync("stock-api", timeout.Token);
            await _app.ResourceNotifications.WaitForResourceHealthyAsync("sm-store", timeout.Token);
            await _app.ResourceNotifications.WaitForResourceAsync(
                "sm-portal", KnownResourceStates.Running, timeout.Token);

            SmStoreBaseUrl = _app.GetEndpoint("sm-store", "https");
            SmPortalBaseUrl = _app.GetEndpoint("sm-portal", "https");

            SmDatabaseConnectionString = await _app.GetConnectionStringAsync("SMDatabase")
                ?? throw new InvalidOperationException("The SMDatabase resource published no connection string.");
            ApiAuthConnectionString = await _app.GetConnectionStringAsync("DefaultConnection")
                ?? throw new InvalidOperationException("The DefaultConnection resource published no connection string.");

            // One admin for the whole run: every admin journey signs in as the same operator,
            // the way one human working through these by hand would.
            Admin = await IdentityTestSupport.CreateAdminAsync(
                ApiAuthConnectionString, SmDatabaseConnectionString);

            /*
            Prove the bootstrap admin can actually authenticate before any journey depends on
            it.

            Every admin journey signs in through SMPortal, which is WebAssembly: when the API
            refuses the credentials all the browser shows is "Check your email and password",
            with no status code and no body. That sends you looking at Playwright selectors for
            a problem that is two layers down. Asking /token directly, here, turns it into the
            API's own answer.
            */
            using var tokenClient = new HttpClient(new HttpClientHandler
            {
                // The ASP.NET Core development certificate is not trusted in this process, and
                // this is a loopback call to a host this fixture just started.
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });

            var tokenResponse = await tokenClient.PostAsync(
                new Uri(StockApiBaseUrl, "/token"),
                new FormUrlEncodedContent(
                [
                    new KeyValuePair<string, string>("username", Admin.Email),
                    new KeyValuePair<string, string>("password", Admin.Password),
                    new KeyValuePair<string, string>("grant_type", "password")
                ]),
                timeout.Token);

            if (!tokenResponse.IsSuccessStatusCode)
            {
                var body = await tokenResponse.Content.ReadAsStringAsync(timeout.Token);

                throw new InvalidOperationException(
                    $"The bootstrap admin {Admin.Email} was created but POST {StockApiBaseUrl}token " +
                    $"answered {(int)tokenResponse.StatusCode} {tokenResponse.StatusCode}: {body}. " +
                    "Every admin journey signs in through that endpoint, so they would all fail " +
                    "on this with no indication that the credentials, and not the UI, are the problem.");
            }
        }
        catch (Exception exception)
        {
            /*
            Recorded rather than rethrown, so one failure here does not fail every test in the
            collection with a fixture-initialization error — a developer gets a reason they
            can act on instead.

            That forgiveness is right on a workstation and wrong in CI. The most likely cause
            here is not a broken product: it is that the developer already has the app host
            running, and StockApi is pinned to 7042 so the second one cannot bind. Failing on
            that would make the suite unrunnable for exactly the person most likely to run it.

            But the same catch would swallow a genuine regression — a broken AppHost.cs, a
            stale DACPAC tripping the staleness guard — and report four green skips to a
            pipeline. So CI sets E2E_REQUIRE_APPHOST=1 and gets the failure it needs; a
            workstation leaves it unset and gets the skip.
            */
            if (RequireAppHost)
            {
                throw;
            }

            StartupFailure = $"Could not start the Aspire app host: {exception}";
            return;
        }

        try
        {
            _playwright = await Playwright.CreateAsync();
            Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }
        catch (Exception exception)
        {
            BrowserUnavailable =
                "Chromium did not launch, which usually means the Playwright browser is not " +
                "installed. From StockManager.E2ETests, run: " +
                "pwsh bin/Debug/net10.0/playwright.ps1 install chromium -- " +
                $"original error: {exception.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.CloseAsync();
        }

        _playwright?.Dispose();

        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    /// <summary>
    /// A fresh, isolated browsing session. Every journey gets its own so a cookie from one
    /// test can never leak into another.
    /// </summary>
    public Task<IBrowserContext> NewContextAsync() => Browser.NewContextAsync(new BrowserNewContextOptions
    {
        // The ASP.NET Core dev certificate covers "localhost" and nothing else.
        // CrossTenantRefusalJourneyTests deliberately reaches the same sm-store endpoint as
        // "127.0.0.1" to get a second Host header out of a real browser without editing a
        // hosts file, which fails certificate hostname validation by design -- this waives
        // that check for every context these tests open, dev-only exactly as the certificate
        // it is waiving is.
        IgnoreHTTPSErrors = true
    });
}
