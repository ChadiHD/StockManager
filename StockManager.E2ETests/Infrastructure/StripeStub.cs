using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace StockManager.E2ETests.Infrastructure;

/// <summary>
/// A stand-in for the two Stripe calls the storefront makes, and its hosted page (T13).
/// </summary>
/// <remarks>
/// A test cannot hold a real store's keys, and must not reach Stripe. The storefront is pointed
/// here through <c>Payments:Stripe:ApiBase</c> and <c>CheckoutOrigin</c>, so everything on the
/// storefront's side is real: the form it posts, the redirect and the CSP that has to allow it, the
/// return, and the webhook, which the journey signs as Stripe would. This stub only records what it
/// was asked for.
/// </remarks>
public sealed class StripeStub : IAsyncDisposable
{
    private readonly WebApplication _app;

    public ConcurrentQueue<StubSession> Sessions { get; } = new();

    public ConcurrentQueue<string> Expired { get; } = new();

    public Uri BaseUrl { get; private set; } = null!;

    private StripeStub(WebApplication app) => _app = app;

    public static async Task<StripeStub> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var app = builder.Build();
        var stub = new StripeStub(app);

        app.MapPost("/v1/checkout/sessions", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            var id = $"cs_test_{Guid.NewGuid():N}";

            // What the customer would be charged: every line, in minor units, as Stripe adds them.
            var total = 0L;
            for (var i = 0; form.ContainsKey($"line_items[{i}][quantity]"); i++)
            {
                total += long.Parse(form[$"line_items[{i}][quantity]"]!, CultureInfo.InvariantCulture)
                         * long.Parse(form[$"line_items[{i}][price_data][unit_amount]"]!, CultureInfo.InvariantCulture);
            }

            stub.Sessions.Enqueue(new StubSession(
                id, form["client_reference_id"].ToString(), form["success_url"].ToString(), total,
                request.Headers.Authorization.ToString()));

            return Results.Json(new { id, url = new Uri(stub.BaseUrl, $"pay/{id}").ToString() });
        });

        app.MapPost("/v1/checkout/sessions/{id}/expire", (string id) =>
        {
            stub.Expired.Enqueue(id);
            return Results.Json(new { id, status = "expired" });
        });

        app.MapGet("/pay/{id}", (string id) =>
            Results.Content($"<!doctype html><title>Stub checkout</title><h1>Pay for {id}</h1>", "text/html"));

        await app.StartAsync();
        stub.BaseUrl = new Uri(app.Urls.First().TrimEnd('/') + "/");

        return stub;
    }

    /// <summary>The origin the storefront's CSP must allow: scheme, host and port, no path.</summary>
    public string Origin => BaseUrl.GetLeftPart(UriPartial.Authority);

    /// <summary>A Stripe-Signature header for <paramref name="payload"/>, as Stripe computes one.</summary>
    public static string Signature(string payload, string secret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}")));

        return $"t={timestamp},v1={signature}";
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}

public sealed record StubSession(string Id, string Reference, string SuccessUrl, long TotalMinor, string Authorization);
