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
	@PoNumber nvarchar(50) = NULL,
	/*
	The tax treatment, decided by ITaxRuleSet before this was called and snapshotted onto
	the order here.

	Passed in rather than worked out: reverse charge turns on the customer's country and
	VAT number against the store's, which is a decision tree with a legend per branch, and
	T-SQL is the wrong place for it — TaxRuleSetTests drives the whole table in C# with no
	database. What this procedure adds is the half that needs the database:
	Product.IsTaxable lives beside the line, so the rate reaches only the lines that carry
	the flag.

	Defaulted so a caller that has not been updated raises an untaxed order rather than
	failing. That was the behaviour until T6 and it is visible on the document, which is
	the right direction for a default to be wrong in.
	*/
	@TaxTreatment nvarchar(30) = NULL,
	@TaxLegend nvarchar(200) = NULL,
	@TaxRatePct decimal(5, 2) = 0
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
		                         [PoNumber], [SiteId], [TaxTreatment], [TaxLegend])
		VALUES (@StaffId, @PlacedByContactId, SYSUTCDATETIME(), 0, 0, 0,
		        @Reference, @AccountId, @QuoteId, @Currency, 'Awaiting payment',
		        NULLIF(LTRIM(RTRIM(@PoNumber)), N''), @SiteId, @TaxTreatment, @TaxLegend);

		SET @Id = SCOPE_IDENTITY();

		/*
		No Delisted and no staleness predicate, deliberately. A product the distributor
		dropped still belongs on the order the customer accepted at the price they were
		quoted; DelistedProductHistoryTests is what holds that. The join added for
		IsTaxable below is an INNER JOIN for the same reason it is safe to be one: delisting
		is a flag, not a delete, so the row is still there.

		The rate reaches a line only if its product is taxable.

		That join is the reason the per-line half is here rather than in the caller: the
		flag is a column on dbo.Product, and assessing it in C# would mean reading the
		catalog back for every line of every order to ask a question the database can
		answer in the same statement that copies the line.

		ROUND, half away from zero, matching TaxAssessment.On and PriceResolver and the
		expression in fnCatalog_VisibleProducts. A line taxed by one rounding rule and
		priced by another is two numbers on one document that do not add up.
		*/
		INSERT INTO dbo.PurchaseDetail([PurchaseId], [ProductId], [Quantity], [PurchasePrice],
		                               [VAT], [TaxRatePct])
		SELECT @Id, [l].[ProductId], [l].[Quantity], [l].[NetPrice],
		       CASE WHEN [p].[IsTaxable] = 1
		            THEN ROUND(CAST([l].[Quantity] * [l].[NetPrice] AS decimal(19, 4))
		                       * CAST(@TaxRatePct AS decimal(9, 4)) / 100, 2)
		            ELSE 0 END,
		       CASE WHEN [p].[IsTaxable] = 1 THEN @TaxRatePct ELSE 0 END
		FROM dbo.QuoteLine l
		INNER JOIN dbo.Product p ON p.[Id] = [l].[ProductId]
		WHERE [l].[QuoteId] = @QuoteId;

		DECLARE @SubTotal money = (
			SELECT ISNULL(SUM([Quantity] * [PurchasePrice]), 0)
			FROM dbo.PurchaseDetail
			WHERE [PurchaseId] = @Id);

		DECLARE @Vat money = (
			SELECT ISNULL(SUM([VAT]), 0)
			FROM dbo.PurchaseDetail
			WHERE [PurchaseId] = @Id);

		-- Summed from the lines rather than taken on the order total, so the figure on the
		-- document is the sum of the figures beside it. Rounding the total separately gives
		-- an order whose VAT line does not equal its own lines by a cent or two.
		UPDATE dbo.Purchase
		SET [SubTotal] = @SubTotal,
		    [VAT] = @Vat,
		    [FinalPrice] = @SubTotal + @Vat
		WHERE [Id] = @Id;

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		THROW;
	END CATCH
END
