using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
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
            var appHostBuilder = await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.StockManager_AppHost>();

            // sm-desktop is a real WPF window with no bearing on any of the four journeys
            // here, and unlike smdatabase-schema (AppHost.cs strips its
            // ExplicitStartupAnnotation so the schema deploys automatically), nothing already
            // stops sm-desktop auto-starting. Add the same annotation in the opposite
            // direction so `dotnet test` does not pop a window on whatever desktop runs it.
            var desktop = appHostBuilder.Resources.FirstOrDefault(resource => resource.Name == "sm-desktop");
            desktop?.Annotations.Add(new ExplicitStartupAnnotation());

            _app = await appHostBuilder.BuildAsync();

            /*
            The timeout covers StartAsync as well, and that is not tidiness.

            It used to be created after StartAsync, so a start that never returned was
            unbounded — and that is exactly what happens here: stock-api and sm-store both
            WaitForCompletion(smdatabase-schema), and if the schema resource never completes
            they never start. Observed twice, both times sitting at
            "Waiting for resource 'smdatabase-schema' to complete" for over half an hour with
            E2E_REQUIRE_APPHOST=1 set and a twelve-minute resource timeout configured, because
            neither applied to the call that was actually stuck.

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

                The known cause is smdatabase-schema. CommunityToolkit's SQL project resources
                carry ExplicitStartupAnnotation ("do not start with the app host"), and
                AppHost.cs strips it so the schema deploys on every run — see the comment
                there, which records the same symptom stalling stock-api once before. Under
                DistributedApplicationTestingBuilder that stripping does not take effect, so
                the schema sits unstarted and stock-api and sm-store, which both
                WaitForCompletion on it, never start either.
                */
                throw new TimeoutException(
                    $"The app host did not finish starting within {ResourceHealthyTimeout.TotalMinutes} " +
                    "minutes. The usual cause is smdatabase-schema never reaching Finished: " +
                    "stock-api and sm-store both WaitForCompletion on it, so neither starts and " +
                    "no endpoint is ever published. Check that resource first, and raise " +
                    "E2E_RESOURCE_TIMEOUT_MINUTES only once you have ruled it out.");
            }

            // Named explicitly rather than left implicit behind stock-api's health check: the
            // schema is what the other two wait on, so when it stalls the failure should say
            // so instead of blaming whatever timed out downstream of it.
            await _app.ResourceNotifications.WaitForResourceAsync(
                "smdatabase-schema", KnownResourceStates.Finished, timeout.Token);

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
            Admin = await IdentityTestSupport.CreateAdminAsync(ApiAuthConnectionString);
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
