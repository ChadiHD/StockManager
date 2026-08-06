CREATE PROCEDURE [dbo].[spCustomerGroup_Update]
	@Slug nvarchar(120),
	@Discount int,
	@Terms nvarchar(50),
	@Note nvarchar(500)
AS
BEGIN
	SET NOCOUNT ON;

	UPDATE dbo.CustomerGroup
	SET [Discount] = @Discount,
	    [Terms] = @Terms,
	    [Note] = @Note
	WHERE [Slug] = @Slug;
END
