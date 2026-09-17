-- One product for the storefront's detail page, under the same visibility rules as the
-- listing. Enforced here rather than trusted from the listing: a SKU is guessable and a
-- product hidden from a group by rule must 404 for them however they arrive at it.
--
-- "The same rules" is now literal — dbo.fnCatalog_VisibleProducts is the single predicate the
-- listing, the facet rail and this share, so a product can no longer be absent from a search
-- and reachable by typing its SKU.
CREATE PROCEDURE [dbo].[spCatalog_GetBySku]
	@SiteId int,
	@Sku nvarchar(50),
	@CustomerGroupId int = NULL
AS
BEGIN
	SET NOCOUNT ON;

	-- The same degradation spCatalog_Search applies: a group from another store is treated as
	-- no group, so its visibility rules cannot decide what this store shows.
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

	-- No term, so no relevance filter, and zero discount and margin: the price a detail page
	-- shows comes from PriceResolver like every other, and NetPrice goes unreferenced here.
	SELECT
		[Id], [Sku], [ProductName], [Description], [RetailPrice], [Cost],
		[QuantityInStock], [ProductImage], [Manufacturer], [ManufacturerPartNumber],
		[Ean], [Distributor], [Source], [Badge], [Featured], [LastSynced],
		[CategorySlug], [CategoryName]
	FROM dbo.fnCatalog_VisibleProducts(
		@SiteId, @CustomerGroupId, @HasIncludeRule, NULL, NULL, 0, NULL, NULL, NULL, 0, 0)
	WHERE [Sku] = @Sku;
END
