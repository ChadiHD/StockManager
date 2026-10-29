/*
Applies one payment provider event to a cart order (T13), exactly once.

The event is recorded in dbo.PaymentEvent in the same transaction that applies it. A second
delivery finds the row and changes nothing, which is what makes a replay harmless. Each outcome
is a claim on 'Awaiting payment', like spOrder_ConvertFromQuote's on a quote:

  Paid     the order becomes Paid, order.paid is queued, and so is its fiscal document when the
           store issues them (T14). Any of the order's sessions may pay it, because a payment
           taken must never go unrecorded.
  Failed   the order is cancelled and its stock returned, but only for its current session.
           Expiring an old session, as "Pay now" does, must not cancel the order.
  None     recorded, nothing else: a completed session whose payment is still pending.

Selects what happened: Duplicate, Paid, Cancelled or Unchanged. Unchanged on a Paid outcome
means money arrived for an order that was not awaiting it, which the caller logs for a person.
*/
CREATE PROCEDURE [dbo].[spOrder_ApplyPaymentEvent]
	@SiteId int,
	@EventId nvarchar(100),
	@EventType nvarchar(100),
	@Reference nvarchar(20),
	@SessionId nvarchar(200),
	@Outcome nvarchar(10)
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @Result nvarchar(20) = N'Unchanged', @Id int;

	BEGIN TRY
		BEGIN TRANSACTION;

		-- UPDLOCK and HOLDLOCK on the key, so two deliveries of one event arriving together queue
		-- here rather than both finding nothing and both applying it.
		IF EXISTS (SELECT 1 FROM dbo.PaymentEvent WITH (UPDLOCK, HOLDLOCK) WHERE [EventId] = @EventId)
		BEGIN
			COMMIT TRANSACTION;
			SELECT N'Duplicate' AS [Result];
			RETURN;
		END

		INSERT INTO dbo.PaymentEvent ([EventId], [SiteId], [EventType], [Reference])
		VALUES (@EventId, @SiteId, @EventType, @Reference);

		IF @Outcome = N'Paid'
		BEGIN
			UPDATE dbo.Purchase
			SET [Status] = N'Paid', [PaidUtc] = SYSUTCDATETIME(), @Id = [Id]
			WHERE [Reference] = @Reference
			  AND [SiteId] = @SiteId
			  AND [QuoteId] IS NULL
			  AND [Status] = N'Awaiting payment';

			IF @Id IS NOT NULL
			BEGIN
				SET @Result = N'Paid';

				-- The invoice the sale owes, in this transaction, when the store issues them (T14).
				EXEC dbo.spFiscalDocument_Enqueue @PurchaseId = @Id;

				DECLARE @MailTo nvarchar(256), @MailName nvarchar(200), @Payload nvarchar(max);

				SELECT @MailTo = c.[Email], @MailName = LTRIM(RTRIM(CONCAT(c.[FirstName], N' ', c.[LastName]))),
				       @Payload = (SELECT p.[Reference] AS [reference], p.[FinalPrice] AS [total]
				                   FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
				FROM dbo.Purchase p
				INNER JOIN dbo.Contact c ON c.[Id] = p.[PlacedByContactId]
				WHERE p.[Id] = @Id;

				IF @MailTo IS NOT NULL
				BEGIN
					EXEC dbo.spEmailOutbox_Enqueue
						@SiteId = @SiteId, @ToAddress = @MailTo, @ToName = @MailName,
						@TemplateKey = N'order.paid', @PayloadJson = @Payload;
				END
			END
		END
		ELSE IF @Outcome = N'Failed'
		BEGIN
			UPDATE dbo.Purchase
			SET [Status] = N'Cancelled', @Id = [Id]
			WHERE [Reference] = @Reference
			  AND [SiteId] = @SiteId
			  AND [QuoteId] IS NULL
			  AND [Status] = N'Awaiting payment'
			  AND [PaymentSessionId] = @SessionId;

			IF @Id IS NOT NULL
			BEGIN
				SET @Result = N'Cancelled';
				EXEC dbo.spOrder_ReturnStock @PurchaseId = @Id;
			END
		END

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		THROW;
	END CATCH

	SELECT @Result AS [Result];
END
