/*
Writes one of a store's content pages from the admin screen (T9), and returns it as stored.

It writes the row the storefront renders: the store's own-locale row when one exists, otherwise
the locale-agnostic one, created if need be. Writing the agnostic row while a locale row existed
would save successfully and change nothing a customer sees.

BodyHtml arrives already sanitized: StockApi runs it through an allow-list before it gets here,
because the storefront renders it as markup and the editor puts that within reach of every admin
of the store.

  50110  no title
*/
CREATE PROCEDURE [dbo].[spSiteContent_Save]
	@SiteId int,
	@ContentKey nvarchar(80),
	@Locale nvarchar(10) = NULL,
	@Title nvarchar(200),
	@Lede nvarchar(1000) = NULL,
	@BodyHtml nvarchar(max) = NULL
AS
BEGIN
	SET NOCOUNT ON;

	IF LEN(LTRIM(RTRIM(ISNULL(@Title, N'')))) = 0
	BEGIN
		THROW 50110, 'A page needs a title.', 1;
	END

	DECLARE @Target int = (
		SELECT TOP 1 [Id] FROM dbo.SiteContent
		WHERE [SiteId] = @SiteId AND [ContentKey] = @ContentKey
		  AND ([Locale] = @Locale OR [Locale] IS NULL)
		ORDER BY CASE WHEN [Locale] IS NULL THEN 1 ELSE 0 END);

	IF @Target IS NULL
	BEGIN
		INSERT INTO dbo.SiteContent ([SiteId], [ContentKey], [Locale], [Title], [Lede], [BodyHtml])
		VALUES (@SiteId, @ContentKey, NULL, LTRIM(RTRIM(@Title)),
		        NULLIF(LTRIM(RTRIM(@Lede)), N''), NULLIF(LTRIM(RTRIM(@BodyHtml)), N''));

		SET @Target = SCOPE_IDENTITY();
	END
	ELSE
	BEGIN
		UPDATE dbo.SiteContent
		SET [Title] = LTRIM(RTRIM(@Title)),
		    [Lede] = NULLIF(LTRIM(RTRIM(@Lede)), N''),
		    [BodyHtml] = NULLIF(LTRIM(RTRIM(@BodyHtml)), N''),
		    [LastModified] = SYSUTCDATETIME()
		WHERE [Id] = @Target;
	END

	SELECT [Id], [SiteId], [ContentKey], [Locale], [Title], [Lede], [BodyHtml], [LastModified]
	FROM dbo.SiteContent
	WHERE [Id] = @Target;
END
