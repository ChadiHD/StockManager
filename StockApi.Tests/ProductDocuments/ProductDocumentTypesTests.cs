using FluentAssertions;
using StockManager.ProductDocuments;
using Xunit;

namespace StockApi.Tests.ProductDocuments;

/// <summary>
/// A product's document is recognised by its bytes, and its extension only picks a type within
/// the family the bytes prove.
/// </summary>
public class ProductDocumentTypesTests
{
    private static readonly byte[] Pdf = "%PDF-1.7"u8.ToArray();
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x06, 0x00];
    private static readonly byte[] Ole = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    [Theory]
    [InlineData("datasheet.pdf", ".pdf")]
    // A PDF is a PDF whatever its name says.
    [InlineData("datasheet.docx", ".pdf")]
    public void APdfIsRecognisedByItsBytes(string fileName, string expectedExtension)
    {
        ProductDocumentTypes.TryDetect(Pdf, fileName, out var contentType, out var extension).Should().BeTrue();
        contentType.Should().Be("application/pdf");
        extension.Should().Be(expectedExtension);
    }

    [Theory]
    [InlineData("manual.DOCX", ".docx")]
    [InlineData("prices.xlsx", ".xlsx")]
    public void AnOfficeFileTakesItsTypeFromItsNameWithinTheZipFamily(string fileName, string expectedExtension)
    {
        ProductDocumentTypes.TryDetect(Zip, fileName, out _, out var extension).Should().BeTrue();
        extension.Should().Be(expectedExtension);
    }

    [Theory]
    // A ZIP named like a legacy file, an OLE file named like a modern one, an archive, a page.
    [InlineData("manual.doc", true)]
    [InlineData("manual.docx", false)]
    [InlineData("bundle.zip", true)]
    [InlineData("page.html", true)]
    public void BytesAndNameThatDisagreeAreRefused(string fileName, bool zip)
    {
        ProductDocumentTypes.TryDetect(zip ? Zip : Ole, fileName, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void AStoredNameIsOnlyWhatTheStoreGenerates()
    {
        ProductDocumentTypes.IsStoredName("0123456789abcdef0123456789abcdef.pdf").Should().BeTrue();
        ProductDocumentTypes.IsStoredName("../0123456789abcdef0123456789abcdef.pdf").Should().BeFalse();
        ProductDocumentTypes.IsStoredName("0123456789abcdef0123456789abcdef.png").Should().BeFalse();
    }

    [Fact]
    public void TheDisplayNameLosesItsPathAndControlCharacters()
    {
        ProductDocumentTypes.DisplayName("C:\\Users\\x\\spec\r\nsheet.pdf", ".pdf").Should().Be("specsheet.pdf");
        ProductDocumentTypes.DisplayName("", ".pdf").Should().Be("document.pdf");
    }
}
