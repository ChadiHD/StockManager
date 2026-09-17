using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// Addresses are gated through their account for the site — see the remarks on IAddressData —
/// so siteId reaching every call here is what keeps one store's addresses out of another's.
/// </summary>
public class AddressDataTests
{
    private readonly ISqlDataAccess _sql = Substitute.For<ISqlDataAccess>();
    private readonly AddressData _data;

    public AddressDataTests()
    {
        _data = new AddressData(_sql);

        // Insert re-reads through GetByAccount, and Delete reads its row count the same way
        // spAccount_Approve does. An unconfigured LoadData returns null, and either the
        // Where/OrderByDescending/FirstOrDefault chain or the bare FirstOrDefault would throw
        // before the call under test is ever inspected.
        _sql.LoadData<AddressModel, object>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>())
            .Returns(new List<AddressModel>());
        _sql.LoadData<int, object>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>())
            .Returns(new List<int>());
    }

    private static bool Site(object parameters, int siteId) =>
        SqlParameterMatch.Has(parameters, "SiteId", siteId);

    [Fact]
    public void GetByAccountPassesTheGivenSiteIdToTheStoredProcedure()
    {
        _data.GetByAccount(5, 7);

        _sql.Received(1).LoadData<AddressModel, object>(
            "dbo.spAddress_GetByAccount", Arg.Is<object>(p => Site(p, 7)), "SMDatabase");
    }

    [Fact]
    public void InsertPassesTheGivenSiteIdToTheStoredProcedure()
    {
        _data.Insert(new AddressModel { AccountId = 5, Kind = "Billing" }, 7);

        _sql.Received(1).SaveData(
            "dbo.spAddress_Insert", Arg.Is<object>(p => Site(p, 7)), "SMDatabase");
    }

    [Fact]
    public void UpdatePassesTheGivenSiteIdToTheStoredProcedure()
    {
        _data.Update(new AddressModel { Id = 3, AccountId = 5 }, 7);

        _sql.Received(1).SaveData(
            "dbo.spAddress_Update", Arg.Is<object>(p => Site(p, 7)), "SMDatabase");
    }

    [Fact]
    public void DeletePassesTheGivenSiteIdToTheStoredProcedure()
    {
        _data.Delete(3, 7);

        _sql.Received(1).LoadData<int, object>(
            "dbo.spAddress_Delete", Arg.Is<object>(p => Site(p, 7)), "SMDatabase");
    }
}
