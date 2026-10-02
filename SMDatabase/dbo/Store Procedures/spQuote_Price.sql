/*
"Send to customer": the transition that makes a quote decidable.

A claim, like the accept and the reject. One atomic UPDATE whose WHERE and SET share a row
lock, over the two statuses a quote may still be priced from, so a colleague pricing a quote
the customer accepted or rejected a moment earlier cannot undo their decision.

That is not hypothetical. Until T5 this button was a toast over no write at all, and the only
procedure that could have served it -- spQuote_UpdateStatus -- stores whatever status it is
handed with no predicate on the current one. Wired to that, pricing a quote the customer had
just rejected would silently un-reject it, and pricing one they had accepted would put an
order's own source document back into Priced.

Requested and Priced both pass. Re-sending a quote after editing its lines is ordinary work,
and refusing the second press would make an operator wonder which of the two writes landed.

A rowcount of zero therefore means exactly one thing -- the customer decided it first -- and
it is reported rather than thrown, for the reason spQuote_Reject reports its own: the page
shows the quote's real state instead of an error.
*/
CREATE PROCEDURE [dbo].[spQuote_Price]
	@QuoteId int,
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @Priced int;

	BEGIN TRY
		BEGIN TRANSACTION;

		UPDATE dbo.Quote
		SET [Status] = 'Priced'
		WHERE [Id] = @QuoteId
		  AND [SiteId] = @SiteId
		  AND [Status] IN ('Requested', 'Priced');

		SET @Priced = @@ROWCOUNT;

		/*
		"Send to customer" sends, in the transaction that makes the quote decidable.

		Every press queues one, re-sends included: re-sending after an edit is ordinary work, and
		the customer should hear the new price rather than act on the old one. A refused claim
		queues nothing -- the customer decided first, and does not need telling about a price
		they have already answered.

		The value is the lines' net total, the same figure the customer's quote page shows. No
		tax: that is assessed when an order is raised, and a quote that asserted a rate would
		be asserting something nobody has decided.
		*/
		IF @Priced = 1
		BEGIN
			DECLARE @MailTo nvarchar(256), @MailName nvarchar(200);

			SELECT @MailTo = [Email], @MailName = [Name]
			FROM dbo.fnQuote_Recipient(@QuoteId, NULL);

			IF @MailTo IS NOT NULL
			BEGIN
				DECLARE @Payload nvarchar(max) = (
					SELECT [q].[Reference] AS [reference],
					       (SELECT ISNULL(SUM([l].[Quantity] * [l].[NetPrice]), 0)
					        FROM dbo.QuoteLine l WHERE [l].[QuoteId] = [q].[Id]) AS [value],
					       [q].[ExpiresDate] AS [expiresDate]
					FROM dbo.Quote q
					WHERE [q].[Id] = @QuoteId
					FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);

				EXEC dbo.spEmailOutbox_Enqueue
					@SiteId = @SiteId, @ToAddress = @MailTo, @ToName = @MailName,
					@TemplateKey = N'quote.priced', @PayloadJson = @Payload;
			END
		END

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		THROW;
	END CATCH

	SELECT @Priced AS [Priced];
END
