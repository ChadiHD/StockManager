-- Resolves one content page for a store, preferring the requested locale and falling back to
-- the locale-agnostic row. Ordering on a computed rank rather than two queries keeps it a
-- single round trip on a path that runs for every editorial page view.
CREATE PROCEDURE [dbo].[spSiteContent_GetByKey]
	@SiteId int,
	@ContentKey nvarchar(80),
	@Locale nvarchar(10) = NULL
AS
BEGIN
	SET NOCOUNT ON;

	SELECT TOP 1 [Id], [SiteId], [ContentKey], [Locale], [Title], [Lede], [BodyHtml], [LastModified]
	FROM [dbo].[SiteContent]
	WHERE [SiteId] = @SiteId
	  AND [ContentKey] = @ContentKey
	  AND ([Locale] = @Locale OR [Locale] IS NULL)
	ORDER BY CASE WHEN [Locale] IS NULL THEN 1 ELSE 0 END;
END
