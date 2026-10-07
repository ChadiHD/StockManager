/*
Creates a store category (@Id NULL) or edits one, and returns its id (T9). Until then the
Categories screen mapped feed categories onto store categories that nobody could create; the
E2E suite inserted them by SQL.

There is no delete. A category may be mapped, hold a product override or sit in a customer's
bookmark, and deactivating it — which dbo.fnSite_ProductPlacement already honours — takes it off
the store without breaking any of them.

  50100  a slug another of this store's categories already uses
  50101  a slug change on a store that is live: /catalog?cat={slug} is in customers' bookmarks
         and in search engines, so it is fixed once the store is active
  50102  no such category on this store
  50103  no name
*/
CREATE PROCEDURE [dbo].[spSiteCategory_Save]
	@SiteId int,
	@Id int = NULL,
	@Slug nvarchar(80),
	@Name nvarchar(120),
	@Blurb nvarchar(400) = NULL,
	@SortOrder int = 0,
	@IsActive bit = 1
AS
BEGIN
	SET NOCOUNT ON;

	IF LEN(LTRIM(RTRIM(ISNULL(@Name, N'')))) = 0
	BEGIN
		THROW 50103, 'A category needs a name.', 1;
	END

	IF @Id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.SiteCategory WHERE [Id] = @Id AND [SiteId] = @SiteId)
	BEGIN
		THROW 50102, 'There is no such category on this store.', 1;
	END

	IF EXISTS (SELECT 1 FROM dbo.SiteCategory
	           WHERE [SiteId] = @SiteId AND [Slug] = @Slug AND (@Id IS NULL OR [Id] <> @Id))
	BEGIN
		THROW 50100, 'Another of this store''s categories already uses that address.', 1;
	END

	IF @Id IS NOT NULL
	   AND EXISTS (SELECT 1 FROM dbo.Site WHERE [Id] = @SiteId AND [IsActive] = 1)
	   AND EXISTS (SELECT 1 FROM dbo.SiteCategory WHERE [Id] = @Id AND [Slug] <> @Slug)
	BEGIN
		THROW 50101, 'The address cannot change while the store is live: customers have it bookmarked. Make a new category instead.', 1;
	END

	IF @Id IS NULL
	BEGIN
		INSERT INTO dbo.SiteCategory ([SiteId], [Slug], [Name], [Blurb], [SortOrder], [IsActive])
		VALUES (@SiteId, @Slug, LTRIM(RTRIM(@Name)), NULLIF(LTRIM(RTRIM(@Blurb)), N''), @SortOrder, @IsActive);

		SELECT CAST(SCOPE_IDENTITY() AS int);
		RETURN;
	END

	UPDATE dbo.SiteCategory
	SET [Slug] = @Slug,
	    [Name] = LTRIM(RTRIM(@Name)),
	    [Blurb] = NULLIF(LTRIM(RTRIM(@Blurb)), N''),
	    [SortOrder] = @SortOrder,
	    [IsActive] = @IsActive
	WHERE [Id] = @Id AND [SiteId] = @SiteId;

	SELECT @Id;
END
