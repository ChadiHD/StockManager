using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace StockApi.Tests.Security;

// In Azure both hosts reach SQL with Authentication=Active Directory Default, which SqlClient 7
// serves only when Microsoft.Data.SqlClient.Extensions.Azure is present. Local SQL Server never
// asks for Entra sign-in, so without this nothing here fails when that package goes: the first
// staging deploy found it missing, as both apps crash-looping at startup.
public class SqlEntraSignInTests
{
    [Fact]
    public void Active_Directory_Default_has_a_provider()
    {
        SqlAuthenticationProvider.GetProvider(SqlAuthenticationMethod.ActiveDirectoryDefault)
            .Should().NotBeNull();
    }
}
