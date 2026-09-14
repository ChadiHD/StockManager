CREATE PROCEDURE [dbo].[spQuote_Insert]
	@Id int output,
	@Reference nvarchar(20) output,
	@AccountId int,
	@Currency nvarchar(3),
	@ExpiresDate datetime2
AS
BEGIN
	SET NOCOUNT ON;

	-- Derived, not passed: a quote belongs to the store its account belongs to, and no caller
	-- is in a position to say otherwise. fnSite_Resolve covers an account row that predates
	-- site scoping and was never backfilled.
	DECLARE @SiteId int = [dbo].[fnSite_Resolve](
		(SELECT [SiteId] FROM dbo.Account WHERE [Id] = @AccountId));

	IF @SiteId IS NULL
	BEGIN
		THROW 50002, 'SiteId is required: the account has no site and this database has more than one, so the store to file this quote under cannot be inferred.', 1;
	END

	SET @Reference = CONCAT('QT-', FORMAT(NEXT VALUE FOR dbo.QuoteReferenceSequence, '0000'));

	INSERT INTO dbo.Quote([Reference], [AccountId], [Currency], [Status], [ExpiresDate], [SiteId])
	VALUES (@Reference, @AccountId, @Currency, 'Requested', @ExpiresDate, @SiteId);

	SELECT @Id = SCOPE_IDENTITY();
END
