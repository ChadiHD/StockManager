-- Everything a contact's own details page can change. IdentityUserId is deliberately absent:
-- the login a contact signs in as is set once by registration and is not editable, because
-- moving it would hand one person's session to another person's account.
CREATE PROCEDURE [dbo].[spContact_Update]
	@Id int,
	@FirstName nvarchar(100),
	@LastName nvarchar(100),
	@Email nvarchar(256),
	@Phone nvarchar(50),
	@RoleInAccount nvarchar(20),
	@IsPrimary bit,
	@Status nvarchar(20),
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @AccountId int =
		(SELECT [k].[AccountId]
		 FROM dbo.Contact k
		 INNER JOIN dbo.Account a ON a.[Id] = k.[AccountId] AND a.[SiteId] = @SiteId
		 WHERE [k].[Id] = @Id);

	IF @AccountId IS NULL
	BEGIN
		-- Not found, or not this store's. The caller cannot tell the two apart, which is the
		-- intent.
		RETURN;
	END

	IF @IsPrimary = 1
	BEGIN
		UPDATE dbo.Contact SET [IsPrimary] = 0
		WHERE [AccountId] = @AccountId AND [IsPrimary] = 1 AND [Id] <> @Id;
	END

	UPDATE dbo.Contact
	SET [FirstName] = @FirstName,
	    [LastName] = @LastName,
	    [Email] = @Email,
	    [Phone] = @Phone,
	    [RoleInAccount] = @RoleInAccount,
	    [IsPrimary] = @IsPrimary,
	    [Status] = @Status
	WHERE [Id] = @Id;
END
