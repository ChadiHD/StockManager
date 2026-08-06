CREATE PROCEDURE [dbo].[spCustomerGroup_Insert]
	@Id int output,
	@Name nvarchar(100),
	@Slug nvarchar(120),
	@Discount int,
	@Terms nvarchar(50),
	@Note nvarchar(500)
AS
BEGIN
	SET NOCOUNT ON;

	INSERT INTO dbo.CustomerGroup([Name], [Slug], [Discount], [Terms], [Note])
	VALUES (@Name, @Slug, @Discount, @Terms, @Note);

	SELECT @Id = SCOPE_IDENTITY();
END
