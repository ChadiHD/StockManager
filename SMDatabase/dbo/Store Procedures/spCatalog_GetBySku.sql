-- One product for the storefront's detail page, under the same visibility rules as the
-- listing. Enforced here rather than trusted from the listing: a SKU is guessable and a
-- product hidden from a group by rule must 404 for them however they arrive at it.
CREATE PROCEDURE [dbo].[spCatalog_GetBySku]
	@SiteId int,
	@Sku nvarchar(50),
	@CustomerGroupId int = NULL
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @HasIncludeRule bit =
		CASE WHEN @CustomerGroupId IS NULL THEN 0
		     WHEN EXISTS (SELECT 1 FROM dbo.GroupVisibility
		                  WHERE [CustomerGroupId] = @CustomerGroupId AND [Rule] = 'IncludeCategory')
		     THEN 1 ELSE 0 END;

	SELECT
		[p].[Id], [p].[Sku], [p].[ProductName], [p].[Description], [p].[RetailPrice], [p].[Cost],
		[p].[QuantityInStock], [p].[ProductImage], [p].[Manufacturer], [p].[ManufacturerPartNumber],
		[p].[Ean], [p].[Distributor], [p].[Source], [p].[Badge], [p].[Featured], [p].[LastSynced],
		[c].[Slug] AS [CategorySlug], [c].[Name] AS [CategoryName]
	FROM [dbo].[Product] p
	INNER JOIN [dbo].[CategoryMapping] m
		ON m.[SiteId] = @SiteId
		AND m.[FeedValue] = p.[Category]
	INNER JOIN [dbo].[SiteCategory] c
		ON c.[Id] = m.[SiteCategoryId]
		AND c.[IsActive] = 1
	WHERE p.[Sku] = @Sku
	  AND p.[Published] = 1
	  AND p.[Delisted] = 0
	  AND (@HasIncludeRule = 0 OR EXISTS (
	          SELECT 1 FROM dbo.GroupVisibility g
	          WHERE g.[CustomerGroupId] = @CustomerGroupId
	            AND g.[Rule] = 'IncludeCategory'
	            AND g.[Value] = c.[Slug]))
	  AND NOT EXISTS (
	          SELECT 1 FROM dbo.GroupVisibility g
	          WHERE g.[CustomerGroupId] = @CustomerGroupId
	            AND g.[Rule] = 'ExcludeCategory'
	            AND g.[Value] = c.[Slug])
	  AND NOT EXISTS (
	          SELECT 1 FROM dbo.GroupVisibility g
	          WHERE g.[CustomerGroupId] = @CustomerGroupId
	            AND g.[Rule] = 'ExcludeDistributor'
	            AND g.[Value] = p.[Distributor]);
END
