/*
Records one store's choice about one product: shown, hidden or left to the mapping, where it is
filed, and how it is featured.

Refusals are named THROWs before anything is written, rather than the CHECK or the composite
foreign key catching them: a constraint violation raised inside a procedure reaches the portal
as a 500, and the admin needs to be told which of these it was.

  50070  no product has that SKU
  50071  Visibility is not Show, Hide or nothing
  50072  the category is not one of this store's active categories
  50073  Show, with nowhere to file it

The last is the one worth explaining. A product whose feed category this store does not map has
no category to be listed under, and the catalog joins one for its slugs, facets and breadcrumbs.
Accepted, that choice would be stored and do nothing, and the admin would see "Show" on a product
no customer can find.

The table holds exceptions only, so a choice that matches the defaults deletes the row rather
than storing one that says nothing.
*/
CREATE PROCEDURE [dbo].[spSiteProduct_Set]
	@SiteId int,
	@Sku nvarchar(50),
	@Visibility nvarchar(10) = NULL,
	@SiteCategoryId int = NULL,
	@Featured bit = 0,
	@Badge nvarchar(40) = NULL
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @ProductId int, @FeedCategory nvarchar(50);

	-- Among the products this store may sell (T9): a SKU is unique within a feed, not across
	-- them, and another store's product is not this one's to place.
	SELECT @ProductId = p.[Id], @FeedCategory = p.[Category]
	FROM dbo.Product p
	CROSS APPLY dbo.fnSite_ProductSellable(@SiteId, p.[FeedId], p.[Source], p.[CurrencyCode]) sel
	WHERE p.[Sku] = @Sku AND sel.[Sellable] = 1;

	IF @ProductId IS NULL
	BEGIN
		THROW 50070, 'No product has that SKU.', 1;
	END

	IF @Visibility IS NOT NULL AND @Visibility NOT IN (N'Show', N'Hide')
	BEGIN
		THROW 50071, 'Visibility is Show, Hide, or neither.', 1;
	END

	-- Only a shown product is filed by its own row; for anything else the mapping decides, and
	-- a stored category would be a choice that did nothing.
	IF ISNULL(@Visibility, N'') <> N'Show'
	BEGIN
		SET @SiteCategoryId = NULL;
	END

	IF @SiteCategoryId IS NOT NULL
	   AND NOT EXISTS (SELECT 1 FROM dbo.SiteCategory
	                   WHERE [Id] = @SiteCategoryId AND [SiteId] = @SiteId AND [IsActive] = 1)
	BEGIN
		THROW 50072, 'That category is not one of this store''s active categories.', 1;
	END

	IF @Visibility = N'Show'
	   AND @SiteCategoryId IS NULL
	   AND NOT EXISTS (SELECT 1
	                   FROM dbo.CategoryMapping m
	                   INNER JOIN dbo.SiteCategory c
	                       ON c.[Id] = m.[SiteCategoryId]
	                       AND c.[SiteId] = @SiteId
	                       AND c.[IsActive] = 1
	                   WHERE m.[SiteId] = @SiteId AND m.[FeedValue] = @FeedCategory)
	BEGIN
		THROW 50073, 'Choose a category to show this product under: its feed category is not mapped on this store.', 1;
	END

	SET @Badge = NULLIF(LTRIM(RTRIM(@Badge)), N'');

	IF @Visibility IS NULL AND @Featured = 0 AND @Badge IS NULL
	BEGIN
		DELETE FROM dbo.SiteProduct
		WHERE [SiteId] = @SiteId AND [ProductId] = @ProductId;

		RETURN;
	END

	-- HOLDLOCK so two admins saving the same product at once cannot both take the insert branch
	-- and collide on the primary key.
	MERGE dbo.SiteProduct WITH (HOLDLOCK) AS t
	USING (SELECT @SiteId AS [SiteId], @ProductId AS [ProductId]) AS s
		ON t.[SiteId] = s.[SiteId] AND t.[ProductId] = s.[ProductId]
	WHEN MATCHED THEN
		UPDATE SET [Visibility] = @Visibility,
		           [SiteCategoryId] = @SiteCategoryId,
		           [Featured] = @Featured,
		           [Badge] = @Badge,
		           [UpdatedUtc] = SYSUTCDATETIME()
	WHEN NOT MATCHED THEN
		INSERT ([SiteId], [ProductId], [Visibility], [SiteCategoryId], [Featured], [Badge])
		VALUES (@SiteId, @ProductId, @Visibility, @SiteCategoryId, @Featured, @Badge);
END
