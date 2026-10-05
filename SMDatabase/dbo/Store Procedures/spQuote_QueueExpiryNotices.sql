/*
Queues "your quote expires soon" for every priced quote at this store that expires within
@WithinDays and has not been told yet. Returns how many were queued.

The one message nothing happening triggers, so it needs a clock: QuoteExpiryBackgroundService
calls this once a day per active store. Everything else here is the shape the rest of T6 uses.

The claim is the UPDATE. Its WHERE and SET share the row locks, so two replicas sweeping at
the same hour cannot both stamp the same quote, and the stamp and the messages commit together
-- a sweep that died halfway leaves neither, and the next night picks the quote up again.

Priced only. A Requested quote has no price to lose, and an Accepted or Rejected one has been
answered. Already expired is out too: telling somebody a quote "expires soon" after it has gone
is a message that can only be wrong, and the customer's page already says it has expired.

The window is measured from now rather than by calendar day, so a night the sweep missed costs
nothing but a quote that expired in the gap -- the next run's window still covers the rest.
*/
CREATE PROCEDURE [dbo].[spQuote_QueueExpiryNotices]
	@SiteId int,
	@WithinDays int = 3
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @due TABLE ([QuoteId] int PRIMARY KEY);
	DECLARE @Queued int = 0;

	BEGIN TRY
		BEGIN TRANSACTION;

		UPDATE dbo.Quote
		SET [ExpiryNoticeSentUtc] = SYSUTCDATETIME()
		OUTPUT [inserted].[Id] INTO @due
		WHERE [SiteId] = @SiteId
		  AND [Status] = 'Priced'
		  AND [ExpiryNoticeSentUtc] IS NULL
		  AND [ExpiresDate] > SYSUTCDATETIME()
		  AND [ExpiresDate] <= DATEADD(DAY, @WithinDays, SYSUTCDATETIME());

		/*
		One enqueue per quote rather than one INSERT for all of them, so spEmailOutbox_Enqueue
		stays the table's only writer. A store has a handful of quotes expiring on any one day;
		a loop over a handful is not the cost worth trading that for.
		*/
		DECLARE @QuoteId int = (SELECT MIN([QuoteId]) FROM @due);
		DECLARE @MailTo nvarchar(256), @MailName nvarchar(200), @Payload nvarchar(max);

		WHILE @QuoteId IS NOT NULL
		BEGIN
			-- Reset each time: a SELECT that finds no recipient leaves its variables as they
			-- were, which would send this quote's notice to the previous quote's buyer.
			SELECT @MailTo = NULL, @MailName = NULL;

			SELECT @MailTo = [Email], @MailName = [Name]
			FROM dbo.fnQuote_Recipient(@QuoteId, NULL);

			IF @MailTo IS NOT NULL
			BEGIN
				SET @Payload = (
					SELECT [q].[Reference] AS [reference],
					       (SELECT ISNULL(SUM([l].[Quantity] * [l].[NetPrice]), 0)
					        FROM dbo.QuoteLine l WHERE [l].[QuoteId] = [q].[Id]) AS [value],
					       [q].[ExpiresDate] AS [expiresDate]
					FROM dbo.Quote q
					WHERE [q].[Id] = @QuoteId
					FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);

				EXEC dbo.spEmailOutbox_Enqueue
					@SiteId = @SiteId, @ToAddress = @MailTo, @ToName = @MailName,
					@TemplateKey = N'quote.expiring', @PayloadJson = @Payload;

				SET @Queued += 1;
			END

			SET @QuoteId = (SELECT MIN([QuoteId]) FROM @due WHERE [QuoteId] > @QuoteId);
		END

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		THROW;
	END CATCH

	SELECT @Queued AS [Queued];
END
