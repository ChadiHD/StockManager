/*
Files a feed category under one of this store's categories, or stops selling it here.

The bulk lever over what a store sells. Mapping a feed category puts every product in it on the
store, including the ones tomorrow's feed adds; unmapping takes them all off. Per-product
exceptions are dbo.SiteProduct's.

A NULL category unmaps, rather than being refused, because "not on this store" is an answer the
admin is entitled to give. The category is checked here, before anything is written, so a
foreign one is a named refusal rather than FK_CategoryMapping_ToSiteCategory's 500.
*/
CREATE PROCEDURE [dbo].[spCategoryMapping_Set]
	@SiteId int,
	@FeedValue nvarchar(100),
	@SiteCategoryId int = NULL
AS
BEGIN
	SET NOCOUNT ON;

	IF @SiteCategoryId IS NULL
	BEGIN
		DELETE FROM dbo.CategoryMapping
		WHERE [SiteId] = @SiteId AND [FeedValue] = @FeedValue;

		RETURN;
	END

	IF NOT EXISTS (SELECT 1 FROM dbo.SiteCategory
	               WHERE [Id] = @SiteCategoryId AND [SiteId] = @SiteId AND [IsActive] = 1)
	BEGIN
		THROW 50072, 'That category is not one of this store''s active categories.', 1;
	END

	MERGE dbo.CategoryMapping WITH (HOLDLOCK) AS t
	USING (SELECT @SiteId AS [SiteId], @FeedValue AS [FeedValue]) AS s
		ON t.[SiteId] = s.[SiteId] AND t.[FeedValue] = s.[FeedValue]
	WHEN MATCHED THEN
		UPDATE SET [SiteCategoryId] = @SiteCategoryId
	WHEN NOT MATCHED THEN
		INSERT ([SiteId], [FeedValue], [SiteCategoryId])
		VALUES (@SiteId, @FeedValue, @SiteCategoryId);
END
