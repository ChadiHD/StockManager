using SMDataManager.Library.Models;
using SMStore.Registration;
using SMStore.Sites;
using Xunit;

namespace SMStore.Tests;

/// <summary>
/// The provider picks a field set from the site's configured key, and refuses when there is
/// no match. The refusal is the part worth testing: a site quietly falling back to another
/// store's field set would collect the wrong paperwork and apply the wrong validation, and
/// nobody would find out until an application could not be approved.
/// </summary>
public class RegistrationFieldSetProviderTests
{
    private sealed class StubSiteContext(SiteModel site) : ISiteContext
    {
        public SiteModel Site { get; } = site;
        public bool IsResolved => true;
    }

    private static SiteModel SiteWith(string fieldSetKey) => new()
    {
        Id = 1,
        SiteKey = "test",
        Name = "Test store",
        Country = "IE",
        RegistrationFieldSet = fieldSetKey
    };

    private static RegistrationFieldSetProvider ProviderFor(string fieldSetKey) =>
        new(new StubSiteContext(SiteWith(fieldSetKey)), [new EuB2bRegistrationFieldSet()]);

    [Fact]
    public void PicksTheFieldSetNamedByTheSite()
    {
        Assert.IsType<EuB2bRegistrationFieldSet>(ProviderFor("eu-b2b").Current);
    }

    [Fact]
    public void MatchesTheKeyWithoutRegardToCase()
    {
        // Site.RegistrationFieldSet is hand-entered tenant configuration, so "EU-B2B" is a
        // plausible thing to find in it and is not worth failing a whole storefront over.
        Assert.IsType<EuB2bRegistrationFieldSet>(ProviderFor("EU-B2B").Current);
    }

    [Fact]
    public void RefusesAKeyNothingImplements()
    {
        var provider = ProviderFor("us-reseller");

        var exception = Assert.Throws<InvalidOperationException>(() => provider.Current);

        // The message has to name the key and what is available, because the person reading
        // it is looking at a storefront that will not render and a Site row they may not
        // have written.
        Assert.Contains("us-reseller", exception.Message);
        Assert.Contains("eu-b2b", exception.Message);
    }
}
