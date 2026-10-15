using FluentAssertions;
using SMDataManager.Library.Models;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>How a store writes an amount: its locale's format, its currency's symbol.</summary>
public class SiteMoneyTests
{
    private static string Format(string locale, string currency, decimal amount) =>
        // ICU separates symbol and number with a no-break space in some locales.
        SiteMoney.Format(new SiteModel { Locale = locale, CurrencyCode = currency }, amount)
            .Replace(' ', ' ');

    [Fact]
    public void ALocaleWritesItsOwnCurrencyItsOwnWay()
    {
        // Until T10 every currency outside a three-entry table printed its code: "BRL 1.234,56".
        Format("pt-BR", "BRL", 1234.56m).Should().Be("R$ 1.234,56");
    }

    [Theory]
    [InlineData("en-IE", "EUR", "€1,234.56")]
    // A store trading in a currency that is not its locale's still gets that currency's symbol.
    [InlineData("en-IE", "GBP", "£1,234.56")]
    [InlineData("en-GB", "GBP", "£1,234.56")]
    public void TheStoresCurrencyWinsOverTheLocalesDefault(string locale, string currency, string expected)
    {
        Format(locale, currency, 1234.56m).Should().Be(expected);
    }
}
