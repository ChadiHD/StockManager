CREATE PROCEDURE [dbo].[spQuote_Insert]
	@Id int output,
	@Reference nvarchar(20) output,
	@AccountId int,
	@Currency nvarchar(3),
	@ExpiresDate datetime2,
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	-- Derived from the account rather than taken on trust: a quote belongs to the store its
	-- account belongs to. The caller states which store it is acting for so that an account
	-- id from elsewhere is refused rather than quietly dragging its own site along.
	IF NOT EXISTS (SELECT 1 FROM dbo.Account WHERE [Id] = @AccountId AND [SiteId] = @SiteId)
	BEGIN
		THROW 50005, 'That account does not belong to this store.', 1;
	END

	-- fnSite_Resolve covers an account row that predates site scoping and was never
	-- backfilled, which the check above cannot match.
	SET @SiteId = [dbo].[fnSite_Resolve](@SiteId);

	IF @SiteId IS NULL
	BEGIN
		THROW 50002, 'SiteId is required: the account has no site and this database has more than one, so the store to file this quote under cannot be inferred.', 1;
	END

	SET @Reference = CONCAT('QT-', FORMAT(NEXT VALUE FOR dbo.QuoteReferenceSequence, '0000'));

	INSERT INTO dbo.Quote([Reference], [AccountId], [Currency], [Status], [ExpiresDate], [SiteId])
	VALUES (@Reference, @AccountId, @Currency, 'Requested', @ExpiresDate, @SiteId);

	SELECT @Id = SCOPE_IDENTITY();
END
