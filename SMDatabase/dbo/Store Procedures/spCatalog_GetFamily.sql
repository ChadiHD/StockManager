/*
A style and its variants, for the storefront's product page (T11), under the same visibility
rules as the listing.

The first grid is the style itself and every variant this viewer may buy, through
dbo.fnCatalog_VisibleProducts like every other catalogue read: a variant of a style the store
hid, or of one the viewer's group may not see, is not returned. The style comes first. The
second grid is those variants' options, which the page turns into the choice a customer makes.

The group degradation and the staleness cutoff are spCatalog_GetBySku's, for spCatalog_GetBySku's
reasons.
*/
CREATE PROCEDURE [dbo].[spCatalog_GetFamily]
	@SiteId int,
	@ProductId int,
	@CustomerGroupId int = NULL
AS
BEGIN
	SET NOCOUNT ON;

	IF @CustomerGroupId IS NOT NULL
	   AND NOT EXISTS (SELECT 1 FROM dbo.CustomerGroup
	                   WHERE [Id] = @CustomerGroupId AND [SiteId] = @SiteId)
	BEGIN
		SET @CustomerGroupId = NULL;
	END

	DECLARE @HasIncludeRule bit =
		CASE WHEN @CustomerGroupId IS NULL THEN 0
		     WHEN EXISTS (SELECT 1 FROM dbo.GroupVisibility
		                  WHERE [CustomerGroupId] = @CustomerGroupId AND [Rule] = 'IncludeCategory')
		     THEN 1 ELSE 0 END;

	DECLARE @StaleBeforeUtc datetime2 = dbo.fnSite_StaleBeforeUtc(@SiteId);

	SELECT
		[Id], [Sku], [ProductName], [Description], [RetailPrice], [Cost],
		[QuantityInStock], [ProductImage], [Manufacturer], [ManufacturerPartNumber],
		[Ean], [Distributor], [Source], [Badge], [Featured], [LastSynced],
		[CategorySlug], [CategoryName], [ParentProductId], [HasVariants]
	INTO #family
	FROM dbo.fnCatalog_VisibleProducts(
		@SiteId, @CustomerGroupId, @HasIncludeRule, NULL, NULL, 0, NULL, NULL, NULL, 0, 0,
		@StaleBeforeUtc)
	WHERE [Id] = @ProductId OR [ParentProductId] = @ProductId;

	SELECT *
	FROM #family
	ORDER BY CASE WHEN [ParentProductId] IS NULL THEN 0 ELSE 1 END, [Id];

	SELECT [o].[ProductId], [o].[Name], [o].[Value], [o].[SortOrder]
	FROM dbo.ProductOption o
	INNER JOIN #family f ON f.[Id] = [o].[ProductId]
	ORDER BY [o].[ProductId], [o].[SortOrder], [o].[Name];

	DROP TABLE #family;
END
