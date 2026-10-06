/*
For each content key a store has written, the row its storefront renders, for the admin content
screen (T9).

The same choice spSiteContent_GetByKey makes per page — the store's own locale first, the
locale-agnostic row otherwise — so what the screen shows is what a customer reads, and
spSiteContent_Save writes back to that same row.
*/
CREATE PROCEDURE [dbo].[spSiteContent_GetForSite]
	@SiteId int,
	@Locale nvarchar(10) = NULL
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [Id], [SiteId], [ContentKey], [Locale], [Title], [Lede], [BodyHtml], [LastModified]
	FROM (
		SELECT *, ROW_NUMBER() OVER (
			PARTITION BY [ContentKey]
			ORDER BY CASE WHEN [Locale] IS NULL THEN 1 ELSE 0 END) AS [Pick]
		FROM [dbo].[SiteContent]
		WHERE [SiteId] = @SiteId
		  AND ([Locale] = @Locale OR [Locale] IS NULL)
	) AS rendered
	WHERE [Pick] = 1
	ORDER BY [ContentKey];
END
