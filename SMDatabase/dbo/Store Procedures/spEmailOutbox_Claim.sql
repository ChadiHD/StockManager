/*
Takes a batch of messages that are due, for one dispatcher to send.

The same shape as spDistributorFeed_ClaimForSync, for the same reasons. The guard is the one
UPDATE: its WHERE and its SET happen under the row locks it takes, so two dispatchers -- two
replicas of StockApi, which Azure Container Apps will run without being asked -- cannot both
come away holding a row. READPAST lets the second skip what the first has locked instead of
queueing behind it, so two replicas share the work rather than taking turns at it.

Due means Pending and past its NextAttemptUtc, or Sending under a lease that has run out. The
second half is what a host killed mid-send leaves behind; without it that message would stay
in Sending for ever, and nothing would say why. The cost is that a message the dead host had
in fact handed to the transport is sent twice. Delivery is at least once, and that is the
right way round for "your order is confirmed".

Attempts is incremented here rather than on failure, so a message that kills the host on
every attempt still runs out of them.
*/
CREATE PROCEDURE [dbo].[spEmailOutbox_Claim]
	@ClaimToken uniqueidentifier,
	@BatchSize int = 20,
	@LeaseMinutes int = 5
AS
BEGIN
	SET NOCOUNT ON;

	WITH [due] AS (
		SELECT TOP (@BatchSize) *
		FROM dbo.EmailOutbox WITH (ROWLOCK, UPDLOCK, READPAST)
		WHERE ([Status] = N'Pending' AND [NextAttemptUtc] <= SYSUTCDATETIME())
		   OR ([Status] = N'Sending'
		       AND [ClaimedUtc] < DATEADD(MINUTE, -@LeaseMinutes, SYSUTCDATETIME()))
		ORDER BY [NextAttemptUtc], [Id])
	UPDATE [due]
	SET [Status] = N'Sending',
	    [ClaimedUtc] = SYSUTCDATETIME(),
	    [ClaimToken] = @ClaimToken,
	    [Attempts] = [Attempts] + 1
	OUTPUT [inserted].[Id], [inserted].[SiteId], [inserted].[ToAddress], [inserted].[ToName],
	       [inserted].[TemplateKey], [inserted].[PayloadJson], [inserted].[PayloadProtected],
	       [inserted].[Status], [inserted].[Attempts], [inserted].[ClaimToken],
	       [inserted].[CreatedUtc];
END
