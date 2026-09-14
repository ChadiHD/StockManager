using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
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
    public ProductCardTests()
    {
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
    public void TheAddButtonDisablesItselfUntilSomethingHandlesIt()
    {
        // Unwired rather than removed, so a later phase supplies a handler instead of
        // replacing markup — see the comment on ProductCard.OnAdd.
        var cut = RenderComponent<ProductCard>(p => p.Add(x => x.Product, Card()));

        cut.Find(".product-card__add").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void TheAddButtonEnablesOnceAHandlerIsWired()
    {
        var cut = RenderComponent<ProductCard>(p => p
            .Add(x => x.Product, Card())
            .Add(x => x.OnAdd, EventCallback.Factory.Create<string>(this, _ => { })));

        cut.Find(".product-card__add").HasAttribute("disabled").Should().BeFalse();
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
