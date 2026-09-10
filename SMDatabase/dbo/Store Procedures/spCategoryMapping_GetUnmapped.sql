-- Feed category values that no mapping sends anywhere for this store, with how many products
-- each is holding back.
--
-- This is the taxonomy work made visible: every row here is stock the store cannot sell
-- because nobody has filed it yet. The admin mapping screen leads with it, and it is the
-- cheapest check that a newly onboarded distributor has actually been absorbed.
CREATE PROCEDURE [dbo].[spCategoryMapping_GetUnmapped]
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT
		[p].[Category] AS [FeedValue],
		COUNT(*) AS [ProductCount],
		MIN([p].[Distributor]) AS [Distributor]
	FROM [dbo].[Product] p
	WHERE [p].[Category] IS NOT NULL
	  AND [p].[Delisted] = 0
	  AND NOT EXISTS (
	          SELECT 1 FROM [dbo].[CategoryMapping] m
	          WHERE m.[SiteId] = @SiteId
	            AND m.[FeedValue] = [p].[Category])
	GROUP BY [p].[Category]
	ORDER BY COUNT(*) DESC;
END
