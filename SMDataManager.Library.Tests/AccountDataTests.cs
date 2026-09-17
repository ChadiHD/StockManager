using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// AccountData's procedures treat siteId as a security predicate, not a filter — see the
/// remarks on IAccountData. A siteId that does not reach the call is the one bug in this class
/// a compiler cannot catch: the anonymous object still builds, Dapper still runs, and the
/// mistake surfaces as another store's account being readable or writable.
/// </summary>
public class AccountDataTests
{
    private readonly ISqlDataAccess _sql = Substitute.For<ISqlDataAccess>();
    private readonly AccountData _data;

    public AccountDataTests()
    {
        _data = new AccountData(_sql);

        // CreateAccount re-reads through GetAccounts after the insert, and Approve/Reject read
        // their row count the same way. An unconfigured LoadData returns null, and the
        // FirstOrDefault that follows would throw before the call under test is ever inspected.
        _sql.LoadData<AccountModel, object>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>())
            .Returns(new List<AccountModel>());
        _sql.LoadData<int, object>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>())
            .Returns(new List<int>());
    }

    private static bool Site(object parameters, int siteId) =>
        SqlParameterMatch.Has(parameters, "SiteId", siteId);

    [Fact]
    public void GetAccountsPassesTheGivenSiteIdToTheStoredProcedure()
    {
        _data.GetAccounts(7);

        _sql.Received(1).LoadData<AccountModel, object>(
            "dbo.spAccount_GetAll", Arg.Is<object>(p => Site(p, 7)), "SMDatabase");
    }

    [Fact]
    public void GetAccountByIdPassesTheGivenSiteIdToTheStoredProcedure()
    {
        _data.GetAccountById(3, 7);

        _sql.Received(1).LoadData<AccountModel, object>(
            "dbo.spAccount_GetById", Arg.Is<object>(p => Site(p, 7)), "SMDatabase");
    }

    [Fact]
    public void CreateAccountPassesTheGivenSiteIdToTheStoredProcedure()
    {
        _data.CreateAccount(new AccountModel { Company = "Acme Trading" }, 7);

        _sql.Received(1).SaveData(
            "dbo.spAccount_Insert", Arg.Is<object>(p => Site(p, 7)), "SMDatabase");
    }

    [Fact]
    public void UpdateStatusPassesTheGivenSiteIdToTheStoredProcedure()
    {
        _data.UpdateStatus(3, "Suspended", 7);

        _sql.Received(1).SaveData(
            "dbo.spAccount_UpdateStatus", Arg.Is<object>(p => Site(p, 7)), "SMDatabase");
    }

    [Fact]
    public void ApprovePassesTheGivenSiteIdToTheStoredProcedure()
    {
        _data.Approve(3, "reviewer@example.com", customerGroupId: 2, siteId: 7);

        _sql.Received(1).LoadData<int, object>(
            "dbo.spAccount_Approve", Arg.Is<object>(p => Site(p, 7)), "SMDatabase");
    }

    [Fact]
    public void RejectPassesTheGivenSiteIdToTheStoredProcedure()
    {
        _data.Reject(3, "reviewer@example.com", "Insufficient evidence.", siteId: 7);

        _sql.Received(1).LoadData<int, object>(
            "dbo.spAccount_Reject", Arg.Is<object>(p => Site(p, 7)), "SMDatabase");
    }

    [Fact]
    public void UpdateTermsPassesTheGivenSiteIdToTheStoredProcedure()
    {
        _data.UpdateTerms(3, customerGroupId: 2, paymentMethod: "Invoice", paymentTerms: "Net30",
            creditLimit: 1000m, siteId: 7);

        _sql.Received(1).SaveData(
            "dbo.spAccount_UpdateTerms", Arg.Is<object>(p => Site(p, 7)), "SMDatabase");
    }
}
