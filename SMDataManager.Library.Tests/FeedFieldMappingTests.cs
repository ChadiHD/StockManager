using FluentAssertions;
using SMDataManager.Library.Models;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// A feed reads the fields its row maps, and nothing it does not (T9). A blank used to be read as
/// FlexIT's element name, so a second distributor's feed with a blank field read whatever its
/// file held under FlexIT's vocabulary.
/// </summary>
public class FeedFieldMappingTests
{
    [Fact]
    public void ABlankFieldIsUnmappedRatherThanFlexItsName()
    {
        var feed = new DistributorFeedModel
        {
            FieldSku = "PartCode",
            FieldName = "Title",
            FieldCategory = " ",
            FieldSrp = null,
            FieldManufacturer = "Brand"
        };

        var fields = feed.ToSettings("unused").Fields;

        fields.Sku.Should().Be("PartCode");
        fields.Name.Should().Be("Title");
        fields.Manufacturer.Should().Be("Brand");
        fields.Category.Should().BeNull("a blank is not FlexIT's \"Category\"");
        fields.Srp.Should().BeNull("a blank is not FlexIT's \"SRP\"");
        fields.Ean.Should().BeNull();
    }

    [Fact]
    public void AMappedFieldIsReadAsWrittenLessItsSpaces()
    {
        new DistributorFeedModel { FieldSku = "  PartCode " }.ToSettings("unused").Fields.Sku.Should().Be("PartCode");
    }
}
