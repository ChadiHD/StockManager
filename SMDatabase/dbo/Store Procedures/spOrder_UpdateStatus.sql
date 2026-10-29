/*
Moves a portal order to a new status, puts back the stock a cancelled cart order held (T12), and
tells a cart order's customer it has shipped (T13).

An order placed from a cart (QuoteId NULL) took its own stock in spOrder_PlaceFromBasket.
Cancelling it returns that stock, in the statement that claims the change, so two cancels cannot
both return it. Reopening one is refused: it would take the stock a second time with no check
that it is still there. An order from a quote never took stock, and moves as it always did.

Marking a cart order Fulfilled records its tracking number and queues order.shipped, in this
transaction, to the person who placed it. An order from a quote sends nothing new.

Marking any order Paid or Fulfilled queues the fiscal document it owes, when its store issues
them (T14).
*/
CREATE PROCEDURE [dbo].[spOrder_UpdateStatus]
	@Id int,
	@Status nvarchar(30),
	@SiteId int,
	@TrackingNumber nvarchar(100) = NULL
AS
BEGIN
	SET NOCOUNT ON;

	BEGIN TRY
		BEGIN TRANSACTION;

		DECLARE @Changed TABLE ([QuoteId] int NULL, [From] nvarchar(30) NULL);

		UPDATE dbo.Purchase
		SET [Status] = @Status,
		    [TrackingNumber] = CASE WHEN @Status = N'Fulfilled' AND [QuoteId] IS NULL
		                            THEN ISNULL(NULLIF(LTRIM(RTRIM(@TrackingNumber)), N''), [TrackingNumber])
		                            ELSE [TrackingNumber] END
		OUTPUT inserted.[QuoteId], deleted.[Status] INTO @Changed
		WHERE [Id] = @Id
		  AND [Reference] IS NOT NULL
		  AND [SiteId] = @SiteId
		  -- The refusal is in the claim, so a cancel landing between a check and this cannot
		  -- reopen an order whose stock it has just returned.
		  AND NOT (@Status <> N'Cancelled' AND [QuoteId] IS NULL AND [Status] = N'Cancelled');

		IF @@ROWCOUNT = 0
		   AND EXISTS (SELECT 1 FROM dbo.Purchase
		               WHERE [Id] = @Id AND [SiteId] = @SiteId AND [Reference] IS NOT NULL
		                 AND [QuoteId] IS NULL AND [Status] = N'Cancelled')
		BEGIN
			THROW 50136, 'A cancelled order placed from the cart cannot be reopened. Its stock has been returned.', 1;
		END

		IF @Status = N'Cancelled'
		   AND EXISTS (SELECT 1 FROM @Changed
		               WHERE [QuoteId] IS NULL AND ISNULL([From], N'') <> N'Cancelled')
		BEGIN
			EXEC dbo.spOrder_ReturnStock @PurchaseId = @Id;
		END

		-- Paid by hand, or shipped on terms without ever reading Paid: either way a sale that owes
		-- its fiscal document (T14). The enqueue makes one per order, so both calling it is safe.
		IF (@Status = N'Paid' OR @Status = N'Fulfilled') AND EXISTS (SELECT 1 FROM @Changed)
		BEGIN
			EXEC dbo.spFiscalDocument_Enqueue @PurchaseId = @Id;
		END

		IF @Status = N'Fulfilled'
		   AND EXISTS (SELECT 1 FROM @Changed
		               WHERE [QuoteId] IS NULL AND ISNULL([From], N'') <> N'Fulfilled')
		BEGIN
			DECLARE @MailTo nvarchar(256), @MailName nvarchar(200), @Payload nvarchar(max);

			SELECT @MailTo = c.[Email], @MailName = LTRIM(RTRIM(CONCAT(c.[FirstName], N' ', c.[LastName]))),
			       @Payload = (SELECT p.[Reference] AS [reference], p.[TrackingNumber] AS [trackingNumber]
			                   FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
			FROM dbo.Purchase p
			INNER JOIN dbo.Contact c ON c.[Id] = p.[PlacedByContactId]
			WHERE p.[Id] = @Id;

			IF @MailTo IS NOT NULL
			BEGIN
				EXEC dbo.spEmailOutbox_Enqueue
					@SiteId = @SiteId, @ToAddress = @MailTo, @ToName = @MailName,
					@TemplateKey = N'order.shipped', @PayloadJson = @Payload;
			END
		END

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		THROW;
	END CATCH
END
