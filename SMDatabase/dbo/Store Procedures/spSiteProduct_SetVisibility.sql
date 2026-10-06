/*
Shows, hides, or hands back to the mapping a whole selection of products on one store at once.
Returns how many of the SKUs named a product.

The bulk half of spSiteProduct_Set, with the same refusals, and one rule of its own: a bulk Show
must name the category to file the selection under. Per product the mapping may already supply
one; across a selection some will be mapped and some will not, and refusing the whole batch for
the ones that are not is a worse answer than asking for the category up front.

Featured and badge are left as each row had them. A selection is about what is on sale, and
clearing a store's ribbons because they were part of a selection would be a side effect nobody
asked for. Rows that end up saying nothing are deleted, as spSiteProduct_Set does.

One statement each for the write and the tidy-up, inside one transaction, so a selection is
applied whole or not at all.
*/
CREATE PROCEDURE [dbo].[spSiteProduct_SetVisibility]
	@SiteId int,
	@Skus [dbo].[SkuList] READONLY,
	@Visibility nvarchar(10) = NULL,
	@SiteCategoryId int = NULL
AS
BEGIN
	SET NOCOUNT ON;

	IF @Visibility IS NOT NULL AND @Visibility NOT IN (N'Show', N'Hide')
	BEGIN
		THROW 50071, 'Visibility is Show, Hide, or neither.', 1;
	END

	IF ISNULL(@Visibility, N'') <> N'Show'
	BEGIN
		SET @SiteCategoryId = NULL;
	END
	ELSE IF @SiteCategoryId IS NULL
	BEGIN
		THROW 50073, 'Choose a category to show these products under.', 1;
	END

	IF @SiteCategoryId IS NOT NULL
	   AND NOT EXISTS (SELECT 1 FROM dbo.SiteCategory
	                   WHERE [Id] = @SiteCategoryId AND [SiteId] = @SiteId AND [IsActive] = 1)
	BEGIN
		THROW 50072, 'That category is not one of this store''s active categories.', 1;
	END

	DECLARE @Matched int;

	BEGIN TRY
		BEGIN TRANSACTION;

		MERGE dbo.SiteProduct WITH (HOLDLOCK) AS t
		USING (SELECT @SiteId AS [SiteId], p.[Id] AS [ProductId]
		       FROM dbo.Product p
		       INNER JOIN @Skus s ON s.[Sku] = p.[Sku]
		       -- Only this store's to place (T9); a SKU is unique within a feed, not across them.
		       CROSS APPLY dbo.fnSite_ProductSellable(@SiteId, p.[FeedId], p.[Source], p.[CurrencyCode]) sel
		       WHERE sel.[Sellable] = 1) AS s
			ON t.[SiteId] = s.[SiteId] AND t.[ProductId] = s.[ProductId]
		WHEN MATCHED THEN
			UPDATE SET [Visibility] = @Visibility,
			           [SiteCategoryId] = @SiteCategoryId,
			           [UpdatedUtc] = SYSUTCDATETIME()
		WHEN NOT MATCHED AND @Visibility IS NOT NULL THEN
			INSERT ([SiteId], [ProductId], [Visibility], [SiteCategoryId])
			VALUES (s.[SiteId], s.[ProductId], @Visibility, @SiteCategoryId);

		SET @Matched = (SELECT COUNT(*) FROM dbo.Product p INNER JOIN @Skus s ON s.[Sku] = p.[Sku]
		                CROSS APPLY dbo.fnSite_ProductSellable(@SiteId, p.[FeedId], p.[Source], p.[CurrencyCode]) sel
		                WHERE sel.[Sellable] = 1);

		DELETE t
		FROM dbo.SiteProduct t
		INNER JOIN dbo.Product p ON p.[Id] = t.[ProductId]
		INNER JOIN @Skus s ON s.[Sku] = p.[Sku]
		WHERE t.[SiteId] = @SiteId
		  AND t.[Visibility] IS NULL
		  AND t.[Featured] = 0
		  AND t.[Badge] IS NULL;

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		THROW;
	END CATCH

	SELECT @Matched AS [Matched];
END
