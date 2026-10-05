/*
Every feed category the catalog holds, how many live products each files, and which of this
store's categories it maps to, if any.

Built from dbo.Product rather than from the mappings, because the question an admin is answering
is "what is coming in from my distributors, and where does it go". A feed value with no mapping
is exactly the row that matters -- it is a category of products this store is not selling and
may not know exists.

Delisted products are not counted: a category whose every product the distributor dropped is
one nobody needs to map.
*/
CREATE PROCEDURE [dbo].[spCategoryMapping_GetForSite]
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [f].[FeedValue],
	       [f].[Products],
	       [m].[SiteCategoryId],
	       [c].[Name] AS [SiteCategoryName]
	FROM (
		SELECT [Category] AS [FeedValue], COUNT(*) AS [Products]
		FROM dbo.Product
		WHERE [Category] IS NOT NULL AND [Delisted] = 0
		GROUP BY [Category]
	) f
	LEFT JOIN dbo.CategoryMapping m
		ON m.[SiteId] = @SiteId
		AND m.[FeedValue] = [f].[FeedValue]
	LEFT JOIN dbo.SiteCategory c
		ON c.[Id] = m.[SiteCategoryId]
		AND c.[SiteId] = @SiteId
	ORDER BY [f].[FeedValue];
END
