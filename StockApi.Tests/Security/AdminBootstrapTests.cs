using FluentAssertions;
using StockApi.Security;
using Xunit;
using Step = StockApi.Security.AdminBootstrap.Step;

namespace StockApi.Tests.Security;

// What a deployment does at startup about its first admin. The shell around this is
// UserManager calls; the decision is the part with a wrong answer in it.
public class AdminBootstrapTests
{
    [Fact]
    public void OnceAnAdminExistsTheConfiguredValuesDoNothing()
    {
        // Left configured after the first deploy, they must not mint a second admin or reset
        // the first one's password -- whoever can read the deployment's settings is not
        // thereby an admin.
        AdminBootstrap.Decide(anyAdmin: true, "boss@example.test", "Secret-1!", loginExists: true)
            .Step.Should().Be(Step.IgnoreConfigured);

        AdminBootstrap.Decide(anyAdmin: true, email: null, password: null, loginExists: false)
            .Step.Should().Be(Step.Nothing);
    }

    [Fact]
    public void WithNoAdminAndNothingConfiguredItOnlyWarns()
    {
        AdminBootstrap.Decide(anyAdmin: false, email: "", password: null, loginExists: false)
            .Step.Should().Be(Step.WarnNoAdmin);
    }

    [Fact]
    public void AnExistingLoginIsPromotedAndKeepsItsPassword()
    {
        AdminBootstrap.Decide(anyAdmin: false, "staff@example.test", password: null, loginExists: true)
            .Step.Should().Be(Step.Promote);
    }

    [Fact]
    public void ANewAdminNeedsAPassword()
    {
        AdminBootstrap.Decide(anyAdmin: false, "boss@example.test", "Secret-1!", loginExists: false)
            .Step.Should().Be(Step.Create);

        var (step, reason) = AdminBootstrap.Decide(anyAdmin: false, "boss@example.test", password: "", loginExists: false);
        step.Should().Be(Step.Refuse);
        reason.Should().Contain("Admin:BootstrapPassword");
    }

    [Fact]
    public void ACustomerLoginNameIsNeverMadeAnAdmin()
    {
        // /token refuses a site-qualified name, so an admin by that name could never sign in;
        // and a customer login gaining Admin is the one outcome this must not have.
        AdminBootstrap.Decide(anyAdmin: false, "aclitrade|buyer@example.test", "Secret-1!", loginExists: true)
            .Step.Should().Be(Step.Refuse);
    }
}
