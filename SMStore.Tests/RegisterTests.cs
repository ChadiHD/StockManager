using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using SMDataManager.Library.Models;
using SMStore.Accounts;
using SMStore.Registration;
using SMStore.Tests.TestSupport;
using Xunit;
using RegisterPage = SMStore.Components.Pages.Register;

namespace SMStore.Tests;

/// <summary>
/// Register.razor renders a different form for every store, because which fields and
/// documents a store demands is IRegistrationFieldSet's decision. What is worth testing here
/// is that the page actually honours that decision, and that it keeps its one hard promise:
/// a password never comes back out of the form it was typed into.
/// </summary>
public class RegisterTests
{
    private static SiteModel SiteWith(string fieldSetKey) => new()
    {
        Id = 1,
        SiteKey = "test",
        Name = "Test store",
        Country = "IE",
        RegistrationFieldSet = fieldSetKey
    };

    private static HttpContext FormPost(Dictionary<string, StringValues> fields) => new DefaultHttpContext
    {
        Request = { Form = new FormCollection(fields, new FormFileCollection()) }
    };

    [Fact]
    public void RendersOnlyTheFieldsTheFieldSetDoesNotHide()
    {
        var fieldSet = new FakeRegistrationFieldSet("fake", new Dictionary<RegistrationField, RegistrationRequirement>
        {
            [RegistrationField.Company] = RegistrationRequirement.Required,
            [RegistrationField.FirstName] = RegistrationRequirement.Required,
            [RegistrationField.Country] = RegistrationRequirement.Required
            // Everything else -- VatNumber, RegistrationNumber, LastName, Email, Phone, the
            // address lines, Region, PostCode -- is absent, so RequirementFor falls back to
            // Hidden for all of them.
        });
        using var context = new Bunit.TestContext();

        var cut = RegisterPageHarness.Render(context, SiteWith(fieldSet.Key), fieldSet);

        cut.Find("#reg-company");
        cut.Find("#reg-firstname");
        cut.Find("#reg-country");

        cut.FindAll("#reg-vatnumber").Should().BeEmpty();
        cut.FindAll("#reg-lastname").Should().BeEmpty();
        cut.FindAll("#reg-email").Should().BeEmpty();
        cut.FindAll("#reg-phone").Should().BeEmpty();
        cut.FindAll("#reg-addressline1").Should().BeEmpty();
        cut.FindAll("#reg-postcode").Should().BeEmpty();

        // Password and its confirmation are ASP.NET Identity's business, not the field set's --
        // they render regardless of what the store asks for.
        cut.Find("#reg-password");
        cut.Find("#reg-confirm");
    }

    [Fact]
    public void MarksRequiredFieldsWithTheRequiredAttributeAndLeavesOptionalOnesAlone()
    {
        var fieldSet = new FakeRegistrationFieldSet("fake", new Dictionary<RegistrationField, RegistrationRequirement>
        {
            [RegistrationField.Company] = RegistrationRequirement.Required,
            [RegistrationField.AddressLine2] = RegistrationRequirement.Optional
        });
        using var context = new Bunit.TestContext();

        var cut = RegisterPageHarness.Render(context, SiteWith(fieldSet.Key), fieldSet);

        cut.Find("#reg-company").HasAttribute("required").Should().BeTrue();
        cut.Find("label[for='reg-company']").QuerySelector(".field__required").Should().NotBeNull();

        cut.Find("#reg-addressline2").HasAttribute("required").Should().BeFalse();
        cut.Find("label[for='reg-addressline2']").QuerySelector(".field__required").Should().BeNull();
    }

    [Fact]
    public void RendersOneFileInputPerDocumentRequirement()
    {
        RegistrationDocumentRequirement[] documents =
        [
            new("ChamberOfCommerce", "Company registration document", true, "A certificate of incorporation."),
            new("VatCertificate", "VAT registration certificate", false, "Speeds up approval.")
        ];
        var fieldSet = new FakeRegistrationFieldSet(
            "fake", new Dictionary<RegistrationField, RegistrationRequirement>(), documents);
        using var context = new Bunit.TestContext();

        var cut = RegisterPageHarness.Render(context, SiteWith(fieldSet.Key), fieldSet);

        var fileInputs = cut.FindAll("input[type=file]");
        fileInputs.Should().HaveCount(2);

        var required = cut.Find("#doc-ChamberOfCommerce");
        required.GetAttribute("name").Should().Be("doc_ChamberOfCommerce");
        cut.Find("label[for='doc-ChamberOfCommerce']").QuerySelector(".field__required").Should().NotBeNull();

        var optional = cut.Find("#doc-VatCertificate");
        optional.GetAttribute("name").Should().Be("doc_VatCertificate");
        cut.Find("label[for='doc-VatCertificate']").QuerySelector(".field__required").Should().BeNull();
    }

    [Fact]
    public void NeverEchoesAPasswordBackIntoTheRenderedHtmlAfterAValidationFailure()
    {
        const string secret = "Sup3rSecretValue!!";

        var fieldSet = new FakeRegistrationFieldSet("fake", new Dictionary<RegistrationField, RegistrationRequirement>
        {
            [RegistrationField.FirstName] = RegistrationRequirement.Required
        });
        var registrations = Substitute.For<IRegistrationService>();
        registrations.RegisterAsync(Arg.Any<RegistrationSubmission>(), Arg.Any<CancellationToken>())
            .Returns(new RegistrationOutcome(false, [new RegistrationError(null, "The two passwords do not match.")]));

        var httpContext = FormPost(new Dictionary<string, StringValues>
        {
            ["FirstName"] = "Ada",
            ["Password"] = secret,
            ["ConfirmPassword"] = "something else"
        });

        using var context = new Bunit.TestContext();
        var cut = RegisterPageHarness.Render(
            context, SiteWith(fieldSet.Key), fieldSet, registrations: registrations, httpContext: httpContext);

        cut.Find("form.register").Submit();

        // The rest of the form is repopulated as a courtesy -- this is the one field where
        // that courtesy would mean the password sat in the HTML of a page that can be cached,
        // printed, or read over someone's shoulder.
        cut.Markup.Should().NotContain(secret);
        cut.Find("#reg-password").HasAttribute("value").Should().BeFalse();
        cut.Find("#reg-confirm").HasAttribute("value").Should().BeFalse();

        // Contrast: a non-password field is repopulated, so the lack of a password value above
        // is Register.razor's own choice and not just every field losing its value on error.
        cut.Find("#reg-firstname").GetAttribute("value").Should().Be("Ada");

        cut.Find("div.alert.error").TextContent.Should().Contain("The two passwords do not match.");
    }

    [Fact]
    public void ASignedInCustomerIsSentToTheirAccountInsteadOfTheForm()
    {
        var fieldSet = new FakeRegistrationFieldSet("fake", new Dictionary<RegistrationField, RegistrationRequirement>());
        var customer = Substitute.For<ICustomerContext>();
        customer.IsSignedIn.Returns(true);
        customer.Contact.Returns(new ContactModel { FirstName = "Ada", LastName = "Byron" });

        using var context = new Bunit.TestContext();
        var cut = RegisterPageHarness.Render(context, SiteWith(fieldSet.Key), fieldSet, customer: customer);

        cut.Markup.Should().Contain("You already have an account");
        cut.FindAll("form.register").Should().BeEmpty();
    }
}
