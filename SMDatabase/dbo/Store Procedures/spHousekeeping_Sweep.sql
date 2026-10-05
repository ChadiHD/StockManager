/*
Deletes what nobody will read again: anonymous baskets untouched since @AbandonedBasketsBeforeUtc,
and sent mail older than @SentMailBeforeUtc. Returns how many of each went.

Owed since T5 and T6, which both deferred it here with the hosting decision. A basket row exists
for every visitor who ever pressed Add, robots included; an outbox row exists for every message,
carrying an address and the values that went into it.

Anonymous baskets only. A signed-in contact has at most one basket (UQ_Basket_Contact), it is
theirs, and it is what they see when they come back. An anonymous one past the cutoff is a
cookie nobody presents any more: its lines go with it, by ON DELETE CASCADE.

Sent mail only. A dead letter is an operator's to look at, and Pending or Sending rows are still
in flight.

In batches, each its own transaction, so a first run over months of backlog never holds a
long lock on tables the storefront writes on every Add. Global rather than per store: a basket's
age means the same thing at every store, and one pass is one set of batches.
*/
CREATE PROCEDURE [dbo].[spHousekeeping_Sweep]
	@AbandonedBasketsBeforeUtc datetime2,
	@SentMailBeforeUtc datetime2,
	@BatchSize int = 5000
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @Baskets int = 0, @SentMail int = 0, @rows int;

	WHILE 1 = 1
	BEGIN
		DELETE TOP (@BatchSize) FROM dbo.Basket
		WHERE [ContactId] IS NULL
		  AND [UpdatedUtc] < @AbandonedBasketsBeforeUtc;

		SET @rows = @@ROWCOUNT;
		SET @Baskets += @rows;

		IF @rows < @BatchSize BREAK;
	END

	WHILE 1 = 1
	BEGIN
		DELETE TOP (@BatchSize) FROM dbo.EmailOutbox
		WHERE [Status] = 'Sent'
		  AND [SentUtc] < @SentMailBeforeUtc;

		SET @rows = @@ROWCOUNT;
		SET @SentMail += @rows;

		IF @rows < @BatchSize BREAK;
	END

	SELECT @Baskets AS [AbandonedBaskets], @SentMail AS [SentMail];
END
