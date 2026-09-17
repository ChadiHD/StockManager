CREATE PROCEDURE [dbo].[spAddress_Insert]
	@Id int output,
	@AccountId int,
	@Kind nvarchar(20),
	@Line1 nvarchar(200),
	@Line2 nvarchar(200),
	@City nvarchar(100),
	@Region nvarchar(100),
	@PostCode nvarchar(20),
	@Country nvarchar(2),
	@IsDefault bit,
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	IF NOT EXISTS (SELECT 1 FROM dbo.Account WHERE [Id] = @AccountId AND [SiteId] = @SiteId)
	BEGIN
		THROW 50005, 'That account does not belong to this store.', 1;
	END

	-- The first address of a kind is the default whether or not the caller said so: an
	-- account with addresses and no default is a checkout with nothing preselected.
	IF @IsDefault = 0
	   AND NOT EXISTS (SELECT 1 FROM dbo.Address
	                   WHERE [AccountId] = @AccountId AND [Kind] = @Kind AND [IsDefault] = 1)
	BEGIN
		SET @IsDefault = 1;
	END

	-- UQ_Address_OneDefault is a filtered unique index over (AccountId, Kind), so the
	-- incumbent has to step down before this one can be promoted.
	IF @IsDefault = 1
	BEGIN
		UPDATE dbo.Address SET [IsDefault] = 0
		WHERE [AccountId] = @AccountId AND [Kind] = @Kind AND [IsDefault] = 1;
	END

	INSERT INTO dbo.Address([AccountId], [Kind], [Line1], [Line2], [City], [Region],
	                        [PostCode], [Country], [IsDefault])
	VALUES (@AccountId, @Kind, @Line1, @Line2, @City, @Region,
	        @PostCode, @Country, @IsDefault);

	SELECT @Id = SCOPE_IDENTITY();
END
