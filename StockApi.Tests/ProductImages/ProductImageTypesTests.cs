using FluentAssertions;
using StockManager.ProductImages;
using Xunit;

namespace StockApi.Tests.ProductImages;

/// <summary>
/// A product photograph is recognised by its bytes (T11), and only three kinds are: the list a
/// shop window needs, without the PDF a customer's paperwork may be.
/// </summary>
public class ProductImageTypesTests
{
    public static TheoryData<byte[], string, string> Images => new()
    {
        { [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0], "image/png", ".png" },
        { [0xFF, 0xD8, 0xFF, 0xE0], "image/jpeg", ".jpg" },
        { "RIFF\u0010\0\0\0WEBPVP8 "u8.ToArray(), "image/webp", ".webp" }
    };

    [Theory]
    [MemberData(nameof(Images))]
    public void APhotographIsRecognisedByItsLeadingBytes(byte[] bytes, string expectedType, string expectedExtension)
    {
        ProductImageTypes.TryDetect(bytes, out var contentType, out var extension).Should().BeTrue();
        contentType.Should().Be(expectedType);
        extension.Should().Be(expectedExtension);
    }

    [Fact]
    public void APdfIsNotAPhotograph()
    {
        ProductImageTypes.TryDetect("%PDF-1.7 a document"u8, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void ARiffFileThatIsNotWebpIsRefused()
    {
        // RIFF also carries WAV and AVI; the form type is what says which.
        ProductImageTypes.TryDetect("RIFF\u0010\0\0\0WAVEfmt "u8, out _, out _).Should().BeFalse();
        ProductImageTypes.TryDetect("RIFF\u0010\0"u8, out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef.jpg", true)]
    [InlineData("0123456789abcdef0123456789abcdef.webp", true)]
    // Only the shape the store generates names anything in storage.
    [InlineData("0123456789abcdef0123456789abcdef.pdf", false)]
    [InlineData("../0123456789abcdef0123456789abcdef.jpg", false)]
    [InlineData("site/0123456789abcdef0123456789abcdef.jpg", false)]
    [InlineData("0123456789ABCDEF0123456789ABCDEF.jpg", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyAGeneratedNameIsAStoredName(string? name, bool expected)
    {
        ProductImageNames.IsStoredName(name).Should().Be(expected);
    }
}
