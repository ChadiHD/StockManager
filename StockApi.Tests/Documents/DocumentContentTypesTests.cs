using FluentAssertions;
using StockManager.Documents;
using Xunit;

namespace StockApi.Tests.Documents;

/// <summary>
/// Neither the declared Content-Type nor the filename extension is evidence of anything — see
/// the remarks on DocumentContentTypes. What is tested here is that TryDetect goes solely on
/// the bytes, never on anything the uploader claimed.
/// </summary>
public class DocumentContentTypesTests
{
    [Fact]
    public void RecognisesAPdfByItsLeadingBytes()
    {
        var bytes = "%PDF-1.7 rest of file"u8.ToArray();

        DocumentContentTypes.TryDetect(bytes, out var contentType, out var extension).Should().BeTrue();
        contentType.Should().Be("application/pdf");
        extension.Should().Be(".pdf");
    }

    [Fact]
    public void RecognisesAPngByItsLeadingBytes()
    {
        byte[] bytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];

        DocumentContentTypes.TryDetect(bytes, out var contentType, out var extension).Should().BeTrue();
        contentType.Should().Be("image/png");
        extension.Should().Be(".png");
    }

    [Fact]
    public void RecognisesAJpegByItsThreeByteSignatureWithoutNeedingAFullBuffer()
    {
        // JFIF, Exif and raw SOI all disagree on byte four, so three bytes is the whole
        // signature rather than a prefix of a longer one — see the remarks on TryDetect.
        byte[] bytes = [0xFF, 0xD8, 0xFF];

        DocumentContentTypes.TryDetect(bytes, out var contentType, out var extension).Should().BeTrue();
        contentType.Should().Be("image/jpeg");
        extension.Should().Be(".jpg");
    }

    [Fact]
    public void RefusesContentThatMatchesNoKnownSignatureRegardlessOfWhatWasDeclared()
    {
        // TryDetect never receives a declared Content-Type at all — a client's claim about a
        // plain-text file has nothing to disagree with here, which is the point.
        var bytes = "not a real document"u8.ToArray();

        DocumentContentTypes.TryDetect(bytes, out var contentType, out var extension).Should().BeFalse();
        contentType.Should().BeEmpty();
        extension.Should().BeEmpty();
    }

    [Fact]
    public void RefusesASpanTooShortToContainAnySignature()
    {
        byte[] bytes = [0xFF];

        DocumentContentTypes.TryDetect(bytes, out _, out _).Should().BeFalse();
    }
}
