using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using SMDataManager.Library.Models;
using SMStore.Payments;
using Xunit;

namespace SMStore.Tests;

/// <summary>
/// Stripe Checkout as this storefront speaks it (T13): what a session asks for, and which webhook
/// deliveries are believed.
/// </summary>
/// <remarks>
/// The signature and the amounts are the two things worth holding. A webhook believed without its
/// signature marks any order paid for whoever can post JSON. A line in the wrong units charges a
/// hundred times over, or a hundredth.
/// </remarks>
public class StripeCheckoutTests
{
    private const string Secret = "whsec_test_secret";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    private static string Event(string type, string paymentStatus = "paid") =>
        "{\"id\":\"evt_1\",\"type\":\"" + type + "\",\"data\":{\"object\":{\"id\":\"cs_1\"," +
        "\"client_reference_id\":\"SO-0042\",\"payment_status\":\"" + paymentStatus + "\"}}}";

    private static string Header(string payload, DateTimeOffset at, string secret = Secret)
    {
        var t = at.ToUnixTimeSeconds().ToString();
        return $"t={t},v1={StripeCheckout.Sign(payload, t, secret)}";
    }

    private static StripeCheckout Stripe(HttpMessageHandler? handler = null) =>
        new(new HttpClient(handler ?? new Recorder()), Options.Create(new StripeOptions { ApiBase = "https://stripe.test/" }));

    [Fact]
    public void OnlyAnEventSignedWithThisStoresSecretAndRecentlyIsBelieved()
    {
        var payload = Event("checkout.session.completed");
        var stripe = Stripe();

        stripe.ReadEvent(payload, Header(payload, Now), Secret, Now).Should().NotBeNull();

        stripe.ReadEvent(payload, Header(payload, Now, "whsec_another_store"), Secret, Now).Should().BeNull();
        stripe.ReadEvent(payload.Replace("SO-0042", "SO-0043"), Header(payload, Now), Secret, Now)
            .Should().BeNull("a changed body is not the body that was signed");
        stripe.ReadEvent(payload, Header(payload, Now.AddMinutes(-6)), Secret, Now)
            .Should().BeNull("a signature older than five minutes is a replay");
        stripe.ReadEvent(payload, null, Secret, Now).Should().BeNull();

        // While a secret is rolled, Stripe signs with both; either may match.
        var t = Now.ToUnixTimeSeconds().ToString();
        stripe.ReadEvent(payload, $"t={t},v1=deadbeef,v1={StripeCheckout.Sign(payload, t, Secret)}", Secret, Now)
            .Should().NotBeNull();
    }

    [Theory]
    [InlineData("checkout.session.completed", "paid", PaymentOutcome.Paid)]
    [InlineData("checkout.session.completed", "unpaid", PaymentOutcome.None)]
    [InlineData("checkout.session.async_payment_succeeded", "paid", PaymentOutcome.Paid)]
    [InlineData("checkout.session.async_payment_failed", "unpaid", PaymentOutcome.Failed)]
    [InlineData("checkout.session.expired", "unpaid", PaymentOutcome.Failed)]
    public void EachSessionEventMeansOneThingForItsOrder(string type, string paymentStatus, PaymentOutcome outcome)
    {
        var payload = Event(type, paymentStatus);

        var read = Stripe().ReadEvent(payload, Header(payload, Now), Secret, Now)!;

        read.Outcome.Should().Be(outcome);
        read.Reference.Should().Be("SO-0042");
        read.SessionId.Should().Be("cs_1");
    }

    [Fact]
    public void AnEventThisStoreDoesNotActOnHasNoOutcome()
    {
        var payload = Event("charge.refunded");

        Stripe().ReadEvent(payload, Header(payload, Now), Secret, Now)!.Outcome.Should().BeNull();
    }

    [Theory]
    [InlineData(99.90, "BRL", 9990)]
    [InlineData(0.07, "EUR", 7)]
    [InlineData(1500, "JPY", 1500)]
    public void AmountsGoInTheUnitsStripeCountsIn(decimal amount, string currency, long minor) =>
        StripeCheckout.MinorUnits(amount, currency).Should().Be(minor);

    [Fact]
    public async Task ASessionAsksForEachLineInMinorUnitsAndNamesTheOrder()
    {
        var recorder = new Recorder();

        var session = await Stripe(recorder).CreateSessionAsync("sk_test_1", new PaymentRequest(
            "SO-0042", "BRL", "ana@example.test",
            [new PaymentLine("Linen shirt — M", 2, 99.90m), new PaymentLine("Standard", 1, 15m)],
            "https://shop.test/orders/t?paid=1", "https://shop.test/orders/t", Now), CancellationToken.None);

        session.Should().Be(new PaymentSession("cs_new", "https://checkout.test/pay/cs_new"));
        recorder.Path.Should().Be("/v1/checkout/sessions");
        recorder.Headers.Should().Contain("Authorization: Bearer sk_test_1").And.Contain("Stripe-Version: 2024-06-20")
            .And.Contain("Idempotency-Key: SO-0042-");

        var form = Uri.UnescapeDataString(recorder.Body.Replace('+', ' '));
        form.Should().Contain("client_reference_id=SO-0042")
            .And.Contain("line_items[0][price_data][unit_amount]=9990")
            .And.Contain("line_items[0][quantity]=2")
            .And.Contain("line_items[1][price_data][unit_amount]=1500")
            .And.Contain("line_items[0][price_data][currency]=brl")
            .And.NotContain("payment_method_types", "the store's own dashboard decides how its customers pay");
    }

    [Fact]
    public void TheLinesAddUpToTheOrdersTotalOnEitherBasis()
    {
        OrderLineModel[] lines = [new() { Name = "Shirt", Quantity = 2, Price = 40m }];

        // Inclusive: each price has its tax in, so the lines and the delivery are the total.
        var inclusive = new OrderModel { Reference = "SO-1", ShippingName = "Standard", ShippingPrice = 10m, VAT = 16.82m, FinalPrice = 90m };
        PaymentService.Lines(inclusive, lines, inclusive: true)
            .Should().Equal(new PaymentLine("Shirt", 2, 40m), new PaymentLine("Standard", 1, 10m));

        // Exclusive: net lines and delivery, and the tax as a line of its own.
        var exclusive = new OrderModel
        {
            Reference = "SO-2", ShippingName = "Standard", ShippingPrice = 10m, VAT = 20.70m,
            TaxTreatment = "Domestic standard", FinalPrice = 110.70m
        };
        PaymentService.Lines(exclusive, lines, inclusive: false)
            .Should().Equal(new PaymentLine("Shirt", 2, 40m), new PaymentLine("Standard", 1, 10m),
                new PaymentLine("Domestic standard", 1, 20.70m));

        // A total the lines do not reach is charged as one line, never as the lines.
        var odd = new OrderModel { Reference = "SO-3", VAT = 0m, FinalPrice = 81m };
        PaymentService.Lines(odd, lines, inclusive: true).Should().ContainSingle().Which.UnitAmount.Should().Be(81m);
    }

    private sealed class Recorder : HttpMessageHandler
    {
        public string Path { get; private set; } = "";
        public string Headers { get; private set; } = "";
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath;
            Headers = request.Headers.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":"cs_new","url":"https://checkout.test/pay/cs_new"}""", Encoding.UTF8, "application/json")
            };
        }
    }
}
