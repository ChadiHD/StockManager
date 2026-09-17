CREATE PROCEDURE [dbo].[spContact_Insert]
	@Id int output,
	@AccountId int,
	@IdentityUserId nvarchar(450),
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

	IF NOT EXISTS (SELECT 1 FROM dbo.Account WHERE [Id] = @AccountId AND [SiteId] = @SiteId)
	BEGIN
		THROW 50005, 'That account does not belong to this store.', 1;
	END

	-- UQ_Contact_OnePrimary is a filtered unique index, so promoting a second primary fails
	-- rather than quietly leaving two. Demote the incumbent first: the caller asked for this
	-- one to be primary, and an error here would be a worse answer than an obeyed instruction.
	IF @IsPrimary = 1
	BEGIN
		UPDATE dbo.Contact SET [IsPrimary] = 0
		WHERE [AccountId] = @AccountId AND [IsPrimary] = 1;
	END

	INSERT INTO dbo.Contact([AccountId], [IdentityUserId], [FirstName], [LastName], [Email],
	                        [Phone], [RoleInAccount], [IsPrimary], [Status])
	VALUES (@AccountId, @IdentityUserId, @FirstName, @LastName, @Email,
	        @Phone, @RoleInAccount, @IsPrimary, @Status);

	SELECT @Id = SCOPE_IDENTITY();
END
