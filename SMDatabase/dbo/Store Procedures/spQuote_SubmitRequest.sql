/*
Turns a basket into a quote request: writes the quote, its lines, and empties the basket.

One transaction, because the halfway states are all wrong. A quote with no lines is a request
sales cannot answer; lines with no quote are orphans; and a basket left full after a successful
submit invites the customer to submit it again and produces two quotes for one intention.

The account is derived from the contact and the site from the account. Neither is accepted from
the caller: a contact is the only thing a session proves, and everything else follows from it.
*/
CREATE PROCEDURE [dbo].[spQuote_SubmitRequest]
	@ContactId int,
	@SiteId int,
	@Lines [dbo].[QuoteRequestLine] READONLY,
	@CustomerNote nvarchar(1000) = NULL,
	@ExpiresDate datetime2 = NULL,
	@BasketId int = NULL,
	@Id int output,
	@Reference nvarchar(20) output
AS
BEGIN
	SET NOCOUNT ON;

	IF NOT EXISTS (SELECT 1 FROM @Lines)
	BEGIN
		THROW 50030, 'A quote request needs at least one line.', 1;
	END

	DECLARE @AccountId int, @Currency nvarchar(3);

	-- Contact carries no SiteId of its own, only its account's, so the chain is walked here as
	-- it is in spBasket_Claim. A contact belonging to another store reads as "not found".
	SELECT @AccountId = a.[Id], @Currency = a.[Currency]
	FROM dbo.Contact c
	INNER JOIN dbo.Account a ON a.[Id] = c.[AccountId]
	WHERE c.[Id] = @ContactId
	  AND a.[SiteId] = @SiteId;

	IF @AccountId IS NULL
	BEGIN
		THROW 50022, 'That contact does not belong to this store.', 1;
	END

	-- A product from another store, or one hidden from this customer, must not reach a quote
	-- line even if the basket somehow held it. The caller resolved these through
	-- fnCatalog_VisibleProducts already; this is the predicate that does not depend on it
	-- having done so.
	IF EXISTS (
		SELECT 1 FROM @Lines l
		WHERE NOT EXISTS (
			SELECT 1
			FROM dbo.Product p
			INNER JOIN dbo.CategoryMapping m
				ON m.[SiteId] = @SiteId
				AND m.[FeedValue] = p.[Category]
			WHERE p.[Id] = l.[ProductId]))
	BEGIN
		THROW 50031, 'One of those products is not sold by this store.', 1;
	END

	BEGIN TRY
		BEGIN TRANSACTION;

		SET @Reference = CONCAT('QT-', FORMAT(NEXT VALUE FOR dbo.QuoteReferenceSequence, '0000'));

		INSERT INTO dbo.Quote([Reference], [AccountId], [Currency], [Status], [ExpiresDate],
		                      [CustomerNote], [SiteId])
		VALUES (@Reference, @AccountId, ISNULL(@Currency, N'EUR'), 'Requested', @ExpiresDate,
		        NULLIF(LTRIM(RTRIM(@CustomerNote)), N''), @SiteId);

		SET @Id = SCOPE_IDENTITY();

		INSERT INTO dbo.QuoteLine([QuoteId], [ProductId], [Quantity], [ListPrice],
		                          [DiscountPct], [NetPrice])
		SELECT @Id, [ProductId], [Quantity], [ListPrice], [DiscountPct], [NetPrice]
		FROM @Lines;

		-- The basket goes, rather than being emptied line by line: its lines cascade, and a
		-- basket row that outlives its submit is one the customer would find waiting for them.
		-- Scoped to the site and the contact so a caller cannot delete anything else.
		IF @BasketId IS NOT NULL
		BEGIN
			DELETE FROM dbo.Basket
			WHERE [Id] = @BasketId
			  AND [SiteId] = @SiteId
			  AND [ContactId] = @ContactId;
		END

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		THROW;
	END CATCH

	-- SELECTed rather than left in the output parameter: SaveData passes an anonymous object
	-- and Dapper cannot write back through one, so a caller that needs a value out of a
	-- mutation has to read it from a result set. See QuoteData.SubmitRequest.
	SELECT @Reference AS [Reference];
END
