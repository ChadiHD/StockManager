CREATE PROCEDURE [dbo].[spCustomerGroup_Update]
	@Slug nvarchar(120),
	@Discount int,
	@Terms nvarchar(50),
	@Note nvarchar(500),
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	-- Slug is unique per site, not globally, so without @SiteId this would edit whichever
	-- store's "reseller" group happened to match.
	UPDATE dbo.CustomerGroup
	SET [Discount] = @Discount,
	    [Terms] = @Terms,
	    [Note] = @Note
	WHERE [Slug] = @Slug
	  AND [SiteId] = @SiteId;
END
