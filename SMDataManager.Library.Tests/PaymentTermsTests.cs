using FluentAssertions;
using Microsoft.Data.SqlClient;
using SMDataManager.Library.Models;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// Payment terms as a label and as a number, and the constraint that keeps them agreeing.
/// </summary>
/// <remarks>
/// The pure cases need nothing. <c>CK_Account_Terms</c> needs a database, because a check
/// constraint is the only thing that actually stops an account reading "Prepaid" on every
/// screen while the credit check gives it thirty days — no C# test can assert the absence of
/// a write path that has not been written yet.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class PaymentTermsTests
{
    [Theory]
    [InlineData("Prepaid", 0)]
    [InlineData("Net 14", 14)]
    [InlineData("Net 30", 30)]
    [InlineData("Net 45", 45)]
    [InlineData("Net 60", 60)]
    public void EveryOfferedTermCarriesItsDays(string label, int days)
    {
        PaymentTerms.DaysFor(label).Should().Be(days);
        PaymentTerms.IsKnown(label).Should().BeTrue();
    }

    [Theory]
    [InlineData("NET30")]
    [InlineData("net 30")]
    [InlineData("30 days")]
    [InlineData("")]
    public void ATermNoScreenOffersIsNotKnown(string label)
    {
        // Each of these is what a parse of the label would have had to survive, and the
        // reason the number is stored rather than derived at read time.
        PaymentTerms.IsKnown(label).Should().BeFalse();
    }

    [Fact]
    public void OnlyPrepaidExtendsNoCredit()
    {
        PaymentTerms.ExtendsCredit(PaymentTerms.Prepaid).Should().BeFalse();

        PaymentTerms.Credit.Should().NotContain(PaymentTerms.Prepaid)
            .And.OnlyContain(term => PaymentTerms.ExtendsCredit(term));
    }

    [SkippableTheory]
    // The two ways the pair can disagree. Prepaid with days is an account billed on terms it
    // does not have; credit terms with zero days is a credit customer refused at acceptance
    // for a facility they were granted.
    [InlineData("Prepaid", 30)]
    [InlineData("Net 30", 0)]
    public void TheDatabaseRefusesALabelAndADayCountThatDisagree(string label, int days)
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            var insert = () => InsertAccount(connection, transaction, label, days);

            insert.Should().Throw<SqlException>()
                .WithMessage("*CK_Account_Terms*");
        }
    }

    [SkippableTheory]
    [InlineData("Prepaid", 0)]
    [InlineData("Net 60", 60)]
    public void TheDatabaseAcceptsAPairThatAgrees(string label, int days)
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            InsertAccount(connection, transaction, label, days).Should().BeGreaterThan(0);
        }
    }

    private static int InsertAccount(
        SqlConnection connection, SqlTransaction transaction, string label, int days)
    {
        var runId = Guid.NewGuid().ToString("N")[..12];

        using var site = new SqlCommand("""
            INSERT INTO dbo.Site (SiteKey, Name, Domain, Country, CurrencyCode, Locale,
                                  OrderMode, RegistrationFieldSet, PriceDisplay, MinMarginPct,
                                  FeedStaleAfterHours, HideStaleProducts, IsActive)
            OUTPUT INSERTED.Id
            VALUES (@key, N'Terms test store', @domain, N'IE', N'EUR', N'en-IE',
                    N'Rfq', N'eu-b2b', N'Public', 0, 0, 0, 1);
            """, connection, transaction);

        site.Parameters.AddWithValue("@key", $"terms-{runId}");
        site.Parameters.AddWithValue("@domain", $"{runId}.terms.invalid");

        int siteId = (int)site.ExecuteScalar()!;

        using var account = new SqlCommand("""
            INSERT INTO dbo.Account (Reference, Company, Currency, PaymentMethod,
                                     PaymentTerms, PaymentTermsDays, CreditLimit, Status, SiteId)
            OUTPUT INSERTED.Id
            VALUES (@reference, N'Terms test customer', N'EUR', N'Credit',
                    @terms, @days, 10000, N'Approved', @siteId);
            """, connection, transaction);

        account.Parameters.AddWithValue("@reference", $"AC-T{runId[..6]}");
        account.Parameters.AddWithValue("@terms", label);
        account.Parameters.AddWithValue("@days", days);
        account.Parameters.AddWithValue("@siteId", siteId);

        return (int)account.ExecuteScalar()!;
    }
}
