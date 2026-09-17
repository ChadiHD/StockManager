using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using SMStore.Components.Shared;
using Xunit;

namespace SMStore.Tests;

/// <summary>
/// FormField is what makes every form on the storefront associate its label, hint and error
/// the same way. Get the ids wrong here and it is wrong on every form at once, silently —
/// the control still renders, it is just unusable with a screen reader.
/// </summary>
public class FormFieldTests : Bunit.TestContext
{
    [Fact]
    public void AssociatesTheLabelWithTheSuppliedFieldId()
    {
        var cut = RenderComponent<FormField>(p => p
            .Add(x => x.Label, "Company name")
            .Add(x => x.FieldId, "reg-company")
            .AddChildContent("<input class=\"input\" id=\"reg-company\" />"));

        cut.Find("label").GetAttribute("for").Should().Be("reg-company");
    }

    [Fact]
    public void GeneratesAFieldIdWhenNoneIsSupplied()
    {
        // An unlabelled input is unusable with a screen reader and unclickable by anyone who
        // taps the label, so the label must always have something to point at.
        var cut = RenderComponent<FormField>(p => p.Add(x => x.Label, "Company name"));

        var fieldId = cut.Find("label").GetAttribute("for");

        fieldId.Should().NotBeNullOrWhiteSpace();
        Regex.IsMatch(fieldId!, "^f[0-9a-f]{8}$").Should().BeTrue($"'{fieldId}' should look like a generated id");
    }

    [Fact]
    public void RendersTheRequiredMarkerOnlyWhenRequired()
    {
        var required = RenderComponent<FormField>(p => p.Add(x => x.Label, "Company").Add(x => x.Required, true));
        required.Find(".field__required").TextContent.Should().Be("*");

        var optional = RenderComponent<FormField>(p => p.Add(x => x.Label, "Company").Add(x => x.Required, false));
        optional.FindAll(".field__required").Should().BeEmpty();
    }

    [Fact]
    public void DescribedByCombinesTheHintAndErrorIdsWhenBothArePresent()
    {
        var cut = RenderComponent<FormField>(p => p
            .Add(x => x.Label, "VAT number")
            .Add(x => x.FieldId, "reg-vat")
            .Add(x => x.Hint, "For example IE1234567X.")
            .Add(x => x.Error, "That does not look right."));

        cut.Instance.DescribedBy.Should().Be("reg-vat-hint reg-vat-error");
        cut.Find("#reg-vat-hint").TextContent.Should().Be("For example IE1234567X.");
        cut.Find("#reg-vat-error").TextContent.Should().Be("That does not look right.");
    }

    [Fact]
    public void DescribedByIsJustTheHintIdWhenThereIsNoError()
    {
        var cut = RenderComponent<FormField>(p => p
            .Add(x => x.Label, "VAT number")
            .Add(x => x.FieldId, "reg-vat")
            .Add(x => x.Hint, "For example IE1234567X."));

        cut.Instance.DescribedBy.Should().Be("reg-vat-hint");
    }

    [Fact]
    public void DescribedByIsNullWithNeitherAHintNorAnError()
    {
        var cut = RenderComponent<FormField>(p => p.Add(x => x.Label, "First name").Add(x => x.FieldId, "reg-firstname"));

        cut.Instance.DescribedBy.Should().BeNull();
    }

    [Fact]
    public void RendersNoErrorElementAndNoInvalidClassWhenThereIsNoError()
    {
        var cut = RenderComponent<FormField>(p => p.Add(x => x.Label, "First name"));

        cut.FindAll(".validation-message").Should().BeEmpty();
        cut.Find("div.field").ClassName.Should().NotContain("field--invalid");
    }

    [Fact]
    public void MarksTheWrapperInvalidWhenThereIsAnError()
    {
        var cut = RenderComponent<FormField>(p => p.Add(x => x.Label, "First name").Add(x => x.Error, "Required."));

        cut.Find("div.field").ClassName.Should().Contain("field--invalid");
        cut.Find(".validation-message").TextContent.Should().Be("Required.");
    }
}
