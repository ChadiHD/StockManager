-- A store's categories, for navigation and the admin mapping screen. Includes inactive rows
-- so the admin can see and re-enable them; the storefront filters on IsActive itself.
CREATE PROCEDURE [dbo].[spSiteCategory_GetBySite]
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [c].[Id], [c].[SiteId], [c].[Slug], [c].[Name], [c].[Blurb], [c].[SortOrder],
	       [c].[IsActive],
	       -- How many feed values currently land here, so an unmapped or over-mapped category
	       -- is obvious on the admin screen without a second query.
	       (SELECT COUNT(*) FROM [dbo].[CategoryMapping] m WHERE m.[SiteCategoryId] = [c].[Id])
	           AS [MappedFeedValues]
	FROM [dbo].[SiteCategory] c
	WHERE [c].[SiteId] = @SiteId
	ORDER BY [c].[SortOrder], [c].[Name];
END
