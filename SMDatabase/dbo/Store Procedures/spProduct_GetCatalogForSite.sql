/*
The admin's product list, with where each product sits on the store the admin is acting for.

The catalog is shared -- every store sees the same rows -- but whether a row is on sale is a
store's answer, so it comes from dbo.fnSite_ProductPlacement: the same function the storefront
reads. Working "on this store" out again here, or in the portal, would be a second copy of the
rule, and the copies would come to disagree about a product the admin believes is for sale.

The product columns match spProduct_GetAll, which the desktop POS still reads with no site.
*/
CREATE PROCEDURE [dbo].[spProduct_GetCatalogForSite]
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [p].[Id], [p].[ProductName], [p].[Description], [p].[RetailPrice], [p].[QuantityInStock],
	       [p].[IsTaxable], [p].[ProductImage], [p].[Sku], [p].[Category], [p].[Cost], [p].[Source],
	       [p].[Distributor], [p].[DistributorSku], [p].[LastSynced], [p].[Delisted],
	       [p].[Manufacturer], [p].[ManufacturerPartNumber], [p].[Ean], [p].[IcecatAvailable],
	       [p].[ImageSourcedUtc],
	       [pl].[OnStore],
	       [pl].[Reason] AS [Placement],
	       -- Only when it is on sale. A hidden product still has a category its mapping would
	       -- file it under, and showing that beside "Hidden" reads as though it were listed.
	       CASE WHEN [pl].[OnStore] = 1 THEN [pl].[CategoryName] END AS [StoreCategory],
	       [pl].[Visibility],
	       [pl].[OverrideCategoryId],
	       [pl].[Featured],
	       [pl].[Badge]
	FROM [dbo].[Product] p
	CROSS APPLY [dbo].[fnSite_ProductPlacement](@SiteId, [p].[Id], [p].[Category], [p].[FeedId], [p].[Source], [p].[CurrencyCode]) pl
	-- Only what this store may sell at all (T9): another store's feed, or a price in another
	-- currency, is not this admin's to see or place.
	WHERE [pl].[Sellable] = 1
	ORDER BY [p].[ProductName];
END
