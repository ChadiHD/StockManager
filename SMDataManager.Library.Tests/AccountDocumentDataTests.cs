using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// A document row is reached only through an account already scoped to the site — see the
/// remarks on IAccountDocumentData — so siteId reaching every call here is what keeps a
/// reviewer at one store from opening or deciding a document filed at another.
/// </summary>
public class AccountDocumentDataTests
{
    private readonly ISqlDataAccess _sql = Substitute.For<ISqlDataAccess>();
    private readonly AccountDocumentData _data;

    public AccountDocumentDataTests()
    {
        _data = new AccountDocumentData(_sql);

        // GetById and Insert's re-fetch both call FirstOrDefault on the result; an unconfigured
        // LoadData returns null and either would throw before the call under test is inspected.
        _sql.LoadData<AccountDocumentModel, object>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>())
            .Returns(new List<AccountDocumentModel>());
    }

    private static bool Site(object parameters, int siteId) =>
        SqlParameterMatch.Has(parameters, "SiteId", siteId);

    [Fact]
    public void GetByAccountPassesTheGivenSiteIdToTheStoredProcedure()
    {
        _data.GetByAccount(5, 7);

        _sql.Received(1).LoadData<AccountDocumentModel, object>(
            "dbo.spAccountDocument_GetByAccount", Arg.Is<object>(p => Site(p, 7)), "SMDatabase");
    }

    [Fact]
    public void GetByIdPassesTheGivenSiteIdToTheStoredProcedure()
    {
        // GetById is the one query that resolves StoredName — see the remarks on
        // AccountDocumentModel — so a siteId that did not reach it would let a reviewer at one
        // store fetch another store's file by guessing an id.
        _data.GetById(11, 7);

        _sql.Received(1).LoadData<AccountDocumentModel, object>(
            "dbo.spAccountDocument_GetById", Arg.Is<object>(p => Site(p, 7)), "SMDatabase");
    }

    [Fact]
    public void InsertPassesTheGivenSiteIdToTheStoredProcedure()
    {
        _data.Insert(
            new AccountDocumentModel { AccountId = 5, Kind = "VatCertificate", OriginalName = "vat.pdf" }, 7);

        _sql.Received(1).SaveData(
            "dbo.spAccountDocument_Insert", Arg.Is<object>(p => Site(p, 7)), "SMDatabase");
    }

    [Fact]
    public void SetStatusPassesTheGivenSiteIdToTheStoredProcedure()
    {
        _data.SetStatus(11, "Accepted", 7);

        _sql.Received(1).SaveData(
            "dbo.spAccountDocument_SetStatus", Arg.Is<object>(p => Site(p, 7)), "SMDatabase");
    }
}
