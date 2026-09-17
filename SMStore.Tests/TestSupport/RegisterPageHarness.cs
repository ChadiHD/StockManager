using Bunit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using SMStore.Accounts;
using SMStore.Documents;
using SMStore.Registration;
using SMStore.Sites;
using StockManager.Documents;
using StockManager.Notifications;
using RegisterPage = SMStore.Components.Pages.Register;

namespace SMStore.Tests.TestSupport;

/// <summary>
/// Wires up every dependency <see cref="RegisterPage"/> injects, then renders it.
/// </summary>
/// <remarks>
/// Blazor populates every <c>@inject</c> property before a single line of the page runs,
/// regardless of which branch a test cares about — so even a test that only wants to look at
/// which fields are on the page has to satisfy the same DI graph as a test that submits the
/// form. Centralising that wiring once means a new injected dependency is one file to update
/// rather than every test that renders this page.
/// </remarks>
internal static class RegisterPageHarness
{
    public static IRenderedComponent<RegisterPage> Render(
        Bunit.TestContext context,
        SiteModel site,
        IRegistrationFieldSet fieldSet,
        ICustomerContext? customer = null,
        IRegistrationService? registrations = null,
        HttpContext? httpContext = null)
    {
        var siteContext = Substitute.For<ISiteContext>();
        siteContext.Site.Returns(site);
        siteContext.IsResolved.Returns(true);

        customer ??= Substitute.For<ICustomerContext>();
        registrations ??= Substitute.For<IRegistrationService>();

        // DocumentUploadService is a concrete seam, not an interface — the instructions are to
        // mock its collaborators and let the real service run, since the validation order it
        // encodes (bytes before the row, refusal before either) is exactly what would be lost
        // by replacing it wholesale.
        var uploads = new DocumentUploadService(
            Substitute.For<IDocumentStore>(),
            Substitute.For<IAccountDocumentData>(),
            siteContext,
            Options.Create(new DocumentStoreOptions()),
            NullLogger<DocumentUploadService>.Instance);

        context.Services.AddSingleton(siteContext);
        context.Services.AddSingleton(customer);
        context.Services.AddSingleton(new RegistrationFieldSetProvider(siteContext, [fieldSet]));
        context.Services.AddSingleton(registrations);
        context.Services.AddSingleton(uploads);
        context.Services.AddSingleton(Substitute.For<IEmailSender>());

        // The generic argument has to be spelled out: without it, type inference registers
        // this under NullLogger<RegisterPage> rather than ILogger<RegisterPage>, which is the
        // key Register.razor's @inject actually resolves against.
        context.Services.AddSingleton<ILogger<RegisterPage>>(NullLogger<RegisterPage>.Instance);

        return httpContext is null
            ? context.RenderComponent<RegisterPage>()
            : context.RenderComponent<RegisterPage>(parameters => parameters.AddCascadingValue(httpContext));
    }
}
