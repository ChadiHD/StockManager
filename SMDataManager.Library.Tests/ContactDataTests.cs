using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// Contact carries no site of its own — see the remarks on IContactData — so siteId reaching
/// every call here is the only thing standing between a signed-in customer and another store's
/// people.
/// </summary>
public class ContactDataTests
{
    private readonly ISqlDataAccess _sql = Substitute.For<ISqlDataAccess>();
    private readonly ContactData _data;

    public ContactDataTests()
    {
        _data = new ContactData(_sql);

        // Insert re-reads through GetByAccount; an unconfigured LoadData returns null, and the
        // FirstOrDefault that follows would throw before the SaveData call under test is ever
        // inspected.
        _sql.LoadData<ContactModel, object>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>())
            .Returns(new List<ContactModel>());
    }

    private static bool Site(object parameters, int siteId) =>
        SqlParameterMatch.Has(parameters, "SiteId", siteId);

    [Fact]
    public void GetByAccountPassesTheGivenSiteIdToTheStoredProcedure()
    {
        _data.GetByAccount(5, 7);

        _sql.Received(1).LoadData<ContactModel, object>(
            "dbo.spContact_GetByAccount", Arg.Is<object>(p => Site(p, 7)), "SMDatabase");
    }

    [Fact]
    public void GetByIdentityUserPassesTheGivenSiteIdToTheStoredProcedure()
    {
        // This is the check that turns a cookie valid at one store into no session at all when
        // presented to another — see the remarks on IContactData. A siteId that leaked past it
        // unfiltered would defeat the entire point of the method.
        _data.GetByIdentityUser("identity-1", 7);

        _sql.Received(1).LoadData<ContactModel, object>(
            "dbo.spContact_GetByIdentityUser", Arg.Is<object>(p => Site(p, 7)), "SMDatabase");
    }

    [Fact]
    public void InsertPassesTheGivenSiteIdToTheStoredProcedure()
    {
        _data.Insert(new ContactModel { AccountId = 5, Email = "person@example.com" }, 7);

        _sql.Received(1).SaveData(
            "dbo.spContact_Insert", Arg.Is<object>(p => Site(p, 7)), "SMDatabase");
    }

    [Fact]
    public void UpdatePassesTheGivenSiteIdToTheStoredProcedure()
    {
        _data.Update(new ContactModel { Id = 9, AccountId = 5 }, 7);

        _sql.Received(1).SaveData(
            "dbo.spContact_Update", Arg.Is<object>(p => Site(p, 7)), "SMDatabase");
    }
}
