-- Kind is not editable. An address changing from Billing to Shipping would silently move
-- which address a tax treatment was decided from, and the account area offers delete and
-- re-add instead.
CREATE PROCEDURE [dbo].[spAddress_Update]
	@Id int,
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

	DECLARE @AccountId int, @Kind nvarchar(20);

	SELECT @AccountId = [d].[AccountId], @Kind = [d].[Kind]
	FROM dbo.Address d
	INNER JOIN dbo.Account a ON a.[Id] = d.[AccountId] AND a.[SiteId] = @SiteId
	WHERE [d].[Id] = @Id;

	IF @AccountId IS NULL
	BEGIN
		RETURN;
	END

	IF @IsDefault = 1
	BEGIN
		UPDATE dbo.Address SET [IsDefault] = 0
		WHERE [AccountId] = @AccountId AND [Kind] = @Kind AND [IsDefault] = 1 AND [Id] <> @Id;
	END

	UPDATE dbo.Address
	SET [Line1] = @Line1,
	    [Line2] = @Line2,
	    [City] = @City,
	    [Region] = @Region,
	    [PostCode] = @PostCode,
	    [Country] = @Country,
	    [IsDefault] = @IsDefault
	WHERE [Id] = @Id;
END
