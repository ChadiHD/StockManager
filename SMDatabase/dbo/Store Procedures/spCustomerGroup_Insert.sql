CREATE PROCEDURE [dbo].[spCustomerGroup_Insert]
	@Id int output,
	@Name nvarchar(100),
	@Slug nvarchar(120),
	@Discount int,
	@Terms nvarchar(50),
	@Note nvarchar(500),
	@SiteId int = NULL
AS
BEGIN
	SET NOCOUNT ON;

	-- Resolved rather than left NULL; see dbo.fnSite_Resolve. UQ_CustomerGroup_Name and
	-- UQ_CustomerGroup_Slug are both scoped by site, so a NULL here is what stops a second
	-- store from ever having its own "Reseller".
	SET @SiteId = [dbo].[fnSite_Resolve](@SiteId);

	IF @SiteId IS NULL
	BEGIN
		THROW 50002, 'SiteId is required: this database has more than one site, so the store to file this row under cannot be inferred.', 1;
	END

	INSERT INTO dbo.CustomerGroup([Name], [Slug], [Discount], [Terms], [Note], [SiteId])
	VALUES (@Name, @Slug, @Discount, @Terms, @Note, @SiteId);

	SELECT @Id = SCOPE_IDENTITY();
END
