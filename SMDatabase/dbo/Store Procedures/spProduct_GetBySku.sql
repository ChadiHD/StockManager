-- One product the acting store may sell, by SKU, for the admin's product screens. Scoped since
-- T9: a SKU is unique within a feed, not across them, and another store's product — another
-- distributor's price, or another currency — is not this admin's to read or edit.
CREATE PROCEDURE [dbo].[spProduct_GetBySku]
	@SiteId int,
	@Sku nvarchar(50)
AS
BEGIN
	SET NOCOUNT ON;

	SELECT p.[Id], p.[ProductName], p.[Description], p.[RetailPrice], p.[QuantityInStock], p.[IsTaxable], p.[ProductImage],
	       p.[Sku], p.[Category], p.[Cost], p.[Source], p.[Distributor], p.[DistributorSku], p.[LastSynced], p.[Delisted],
	       p.[Manufacturer], p.[ManufacturerPartNumber], p.[Ean], p.[IcecatAvailable], p.[ImageSourcedUtc]
	FROM [dbo].[Product] p
	CROSS APPLY [dbo].[fnSite_ProductSellable](@SiteId, p.[FeedId], p.[Source], p.[CurrencyCode]) sel
	WHERE p.[Sku] = @Sku
	  AND sel.[Sellable] = 1;
END
