using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using SMDataManager.Library.Feeds;
using SMDataManager.Library.Models;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// A CSV feed is read by its header row, and the file is the shape Intec's actually is: a UTF-8
/// byte-order mark, CRLF lines, quoted descriptions carrying commas, and prices with a leading
/// space. The BOM is the one that fails quietly — left in, it becomes part of the first header,
/// so the SKU column matches nothing and every row is dropped as having no SKU.
/// </summary>
public class CsvFeedParsingTests
{
    private static readonly FeedFieldMap IntecFields = new()
    {
        Sku = "Supplier Product no",
        Name = "Description",
        Description = null,
        Category = "Product Group",
        Cost = "Price",
        Srp = null,
        Quantity = "Availability",
        Manufacturer = "Manufacturer",
        Mpn = "Manufacturer product no",
        Ean = null,
        IcecatFlag = null
    };

    private static Stream Utf8WithBom(string text) =>
        new MemoryStream(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray());

    [Fact]
    public void ACsvRowIsMappedByItsHeaders()
    {
        var csv =
            "Supplier Product no,Manufacturer,Manufacturer product no,Description,Product Group,Availability,Price,On order\r\n" +
            "21ABC01UK,Lenovo,21ABC01UK,\"ThinkPad 14, 16GB/512SSD\",Lenovo,39, 1980.00,0\r\n" +
            ",Lenovo,NO-SKU,\"No part number\",Lenovo,1, 10.00,0\r\n";

        var records = SftpDistributorFeedClient.Parse("intec.csv", Utf8WithBom(csv), IntecFields);

        records.Should().ContainSingle("a row with no SKU cannot be matched and is dropped");
        var record = records[0];
        record.DistributorSku.Should().Be("21ABC01UK");
        record.Name.Should().Be("ThinkPad 14, 16GB/512SSD");
        record.Category.Should().Be("Lenovo");
        record.Cost.Should().Be(1980.00m);
        record.Quantity.Should().Be(39);
        record.Manufacturer.Should().Be("Lenovo");
        record.Mpn.Should().Be("21ABC01UK");
        record.Raw.Should().ContainKey("On order", "unmapped columns still reach the Test button's field list");
    }

    [Fact]
    public void AnXmlFileIsStillReadAsXml()
    {
        var xml = "<feed><product><PartNumber>X1</PartNumber><Price>5.50</Price></product>" +
                  "<product><PartNumber>X2</PartNumber><Price>7</Price></product></feed>";

        var records = SftpDistributorFeedClient.Parse(
            "stock.XML", new MemoryStream(Encoding.UTF8.GetBytes(xml)),
            new FeedFieldMap { Sku = "PartNumber", Cost = "Price" });

        records.Select(r => r.DistributorSku).Should().Equal("X1", "X2");
        records[0].Cost.Should().Be(5.50m);
    }
}
