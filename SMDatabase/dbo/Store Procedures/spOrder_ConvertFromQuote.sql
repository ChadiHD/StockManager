-- Converts a quote into a sales order: claims the quote's status transition, copies the quote
-- lines into PurchaseDetail, totals the order, and leaves the quote Accepted.
--
-- Both paths land here. An admin converts from /admin/quotes; a customer accepts their own
-- quote from the storefront. One procedure rather than two, because two would be one guard
-- each and the guards would drift.
CREATE PROCEDURE [dbo].[spOrder_ConvertFromQuote]
	@QuoteId int,
	@Id int output,
	@Reference nvarchar(20) output,
	@SiteId int,
	-- Exactly one of these. Staff when an admin converted it, a contact when the customer
	-- accepted it — see CK_Purchase_Placer for why the column cannot be one field holding
	-- whichever applies.
	@StaffId nvarchar(128) = NULL,
	@PlacedByContactId int = NULL,
	-- The customer's own purchase-order number, if they gave one.
	@PoNumber nvarchar(50) = NULL
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @AccountId int, @Currency nvarchar(3), @QuoteSiteId int;

	IF (CASE WHEN @StaffId IS NULL THEN 0 ELSE 1 END
	  + CASE WHEN @PlacedByContactId IS NULL THEN 0 ELSE 1 END) <> 1
	BEGIN
		THROW 50011, 'An order records exactly one placer: staff or a contact, never both and never neither.', 1;
	END

	-- Site is part of the lookup, so a quote belonging to another store reads as "not found"
	-- rather than converting into an order this store's admin can then see.
	SELECT @AccountId = [AccountId], @Currency = [Currency], @QuoteSiteId = [SiteId]
	FROM dbo.Quote
	WHERE [Id] = @QuoteId
	  AND [SiteId] = @SiteId;

	IF @AccountId IS NULL
	BEGIN
		THROW 50001, 'Quote not found.', 1;
	END

	-- FK_Purchase_ToContact enforces this too, and a foreign-key violation 500s from the
	-- middle of a transaction. Saying it here gives the caller an answer it can act on.
	IF @PlacedByContactId IS NOT NULL
	   AND NOT EXISTS (SELECT 1 FROM dbo.Contact
	                   WHERE [Id] = @PlacedByContactId AND [AccountId] = @AccountId)
	BEGIN
		THROW 50012, 'That contact does not belong to the account this quote was raised for.', 1;
	END

	-- The order stays in the quote's store. fnSite_Resolve only does anything for a quote that
	-- predates site scoping and was never backfilled, which the predicate above cannot match.
	SET @SiteId = [dbo].[fnSite_Resolve](@QuoteSiteId);

	IF @SiteId IS NULL
	BEGIN
		THROW 50002, 'SiteId is required: the quote has no site and this database has more than one, so the store to file this order under cannot be inferred.', 1;
	END

	BEGIN TRY
		BEGIN TRANSACTION;

		/*
		The status transition is the claim, and it comes first for that reason.

		Before T5 this UPDATE sat at the end with no predicate on the current status, so two
		callers converting the same quote produced two orders from it — each with its own SO-
		reference and its own copy of every line. Reachable then by double-clicking Convert;
		ordinary now that the customer has an Accept button of their own and both land here.

		One UPDATE whose WHERE and SET share a row lock: two callers arriving together cannot
		both come away with a rowcount of 1. Reading the status first and converting if it
		looked right is the same code with a race in the gap, and the gap is exactly where an
		admin's Convert and a customer's Accept meet.

		'Requested' is allowed as well as 'Priced' because this procedure's job is to stop a
		double conversion, not to decide who may convert when — an admin converting an unpriced
		quote is a deliberate act. A customer may only accept a quote that has been priced, and
		that rule lives on the customer path where it belongs.
		*/
		UPDATE dbo.Quote
		SET [Status] = 'Accepted'
		WHERE [Id] = @QuoteId
		  AND [SiteId] = @SiteId
		  AND [Status] IN ('Requested', 'Priced');

		IF @@ROWCOUNT = 0
		BEGIN
			THROW 50010, 'That quote is no longer awaiting acceptance.', 1;
		END

		SET @Reference = CONCAT('SO-', FORMAT(NEXT VALUE FOR dbo.OrderReferenceSequence, '0000'));

		INSERT INTO dbo.Purchase([StaffId], [PlacedByContactId], [PurchaseDate],
		                         [SubTotal], [VAT], [FinalPrice],
		                         [Reference], [AccountId], [QuoteId], [Currency], [Status],
		                         [PoNumber], [SiteId])
		VALUES (@StaffId, @PlacedByContactId, SYSUTCDATETIME(), 0, 0, 0,
		        @Reference, @AccountId, @QuoteId, @Currency, 'Awaiting payment',
		        NULLIF(LTRIM(RTRIM(@PoNumber)), N''), @SiteId);

		SET @Id = SCOPE_IDENTITY();

		-- No Delisted and no staleness predicate, deliberately. A product the distributor
		-- dropped still belongs on the order the customer accepted at the price they were
		-- quoted; DelistedProductHistoryTests is what holds that.
		INSERT INTO dbo.PurchaseDetail([PurchaseId], [ProductId], [Quantity], [PurchasePrice], [VAT])
		SELECT @Id, [l].[ProductId], [l].[Quantity], [l].[NetPrice], 0
		FROM dbo.QuoteLine l
		WHERE [l].[QuoteId] = @QuoteId;

		DECLARE @SubTotal money = (
			SELECT ISNULL(SUM([Quantity] * [PurchasePrice]), 0)
			FROM dbo.PurchaseDetail
			WHERE [PurchaseId] = @Id);

		UPDATE dbo.Purchase
		SET [SubTotal] = @SubTotal,
		    [VAT] = 0,
		    [FinalPrice] = @SubTotal
		WHERE [Id] = @Id;

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		THROW;
	END CATCH
END
