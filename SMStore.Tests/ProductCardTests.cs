using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SMDataManager.Library.Models;
using SMStore.Catalog;
using SMStore.Components.Shared;
using SMStore.Ordering;
using SMStore.Sites;
using Xunit;

namespace SMStore.Tests;

/// <summary>
/// ProductCard renders a ProductCardView, which is already resolved by the time it gets here —
/// so what is worth testing is that the card faithfully reflects what the presenter decided,
/// rather than deciding anything of its own.
/// </summary>
public class ProductCardTests : Bunit.TestContext
{
    /// <summary>Renders the antiforgery hidden input a real request would carry.</summary>
    private sealed class StubAntiforgeryStateProvider : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken GetAntiforgeryToken() =>
            new("stub-token-value", "__RequestVerificationToken");
    }

    public ProductCardTests()
    {
        Services.AddSingleton<AntiforgeryStateProvider>(new StubAntiforgeryStateProvider());

        var siteContext = Substitute.For<ISiteContext>();
        siteContext.Site.Returns(new SiteModel { Id = 1, SiteKey = "test", Name = "Test store", OrderMode = RfqOrderingMode.ModeKey });

        Services.AddSingleton(new OrderingModeProvider(siteContext, [new RfqOrderingMode()]));
    }

    private static ProductCardView Card(string? priceLabel = "€10.00", bool inStock = true, string? badge = null) =>
        new("SKU1", "Widget", "Acme", badge, inStock ? "In stock" : "Lead time on request", inStock, priceLabel, null);

    [Fact]
    public void RendersNoPriceWhenThePresenterHidesPrices()
    {
        var cut = RenderComponent<ProductCard>(p => p.Add(x => x.Product, Card(priceLabel: null)));

        // Product.PriceLabel null is the presenter's signal that this viewer may not see a
        // price at all — the card must not render a price element for null to fall back on.
        cut.FindAll(".product-card__amount").Should().BeEmpty();
        cut.Find(".product-card__note").TextContent.Should().Be("Sign in to see pricing");
    }

    [Fact]
    public void RendersTheFormattedPriceWhenThePresenterAllowsIt()
    {
        var cut = RenderComponent<ProductCard>(p => p.Add(x => x.Product, Card(priceLabel: "€10.00")));

        cut.Find(".product-card__amount").TextContent.Should().Be("€10.00");

        // Rfq never shows a payable total, so the note beside a visible price is the
        // volume-pricing caveat rather than "excl. tax".
        cut.Find(".product-card__note").TextContent.Should().Be("Volume pricing on quote");
    }

    [Fact]
    public void TheAddButtonPostsTheSkuToTheBasket()
    {
        var cut = RenderComponent<ProductCard>(p => p.Add(x => x.Product, Card()));

        var form = cut.Find("form.product-card__add-form");

        // A form, not an @onclick. The card used to bind an EventCallback and disable itself
        // until a later phase wired one, but the storefront renders with static SSR and has no
        // circuit for a handler to run on, so that button could never have fired.
        form.GetAttribute("method").Should().Be("post");
        form.GetAttribute("action").Should().Be(BasketEndpoints.AddPath);
        form.QuerySelector("input[type=hidden][name='__RequestVerificationToken']")
            .Should().NotBeNull();

        // The SKU, never a product id: no database id belongs in storefront markup.
        form.QuerySelector("input[type=hidden][name='sku']")!
            .GetAttribute("value").Should().Be("SKU1");
    }

    [Fact]
    public void TheAddFormReturnsToTheListingItWasRenderedOn()
    {
        var cut = RenderComponent<ProductCard>(p => p
            .Add(x => x.Product, Card())
            .Add(x => x.ReturnTo, "/catalog?cat=servers&page=3"));

        // Otherwise adding from page 3 of a filtered listing loses the customer's place in
        // it, which turns browsing into a sequence of back buttons.
        cut.Find("form.product-card__add-form")
            .QuerySelector("input[type=hidden][name='returnUrl']")!
            .GetAttribute("value").Should().Be("/catalog?cat=servers&page=3");
    }

    [Fact]
    public void RendersABadgeOnlyWhenTheProductHasOne()
    {
        var withBadge = RenderComponent<ProductCard>(p => p.Add(x => x.Product, Card(badge: "New")));
        withBadge.FindAll(".product-card__badge").Should().HaveCount(1);

        var withoutBadge = RenderComponent<ProductCard>(p => p.Add(x => x.Product, Card(badge: null)));
        withoutBadge.FindAll(".product-card__badge").Should().BeEmpty();
    }
}
