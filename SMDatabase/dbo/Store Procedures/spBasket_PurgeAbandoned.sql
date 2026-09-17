/*
Deletes anonymous baskets nobody has touched for a while.

The cost of finding a basket by a cookie rather than by client storage: a row per visitor who
adds something, robots included. Nothing here is a loss — an anonymous basket is a list, not a
record — and a signed-in contact's basket is never swept, because that is the one somebody
expects to find when they come back next month.

Idempotent and bounded. @Take exists so a first run against a database that has never been
swept cannot take a lock on tens of thousands of rows at once; the caller loops until it
reports zero.
*/
CREATE PROCEDURE [dbo].[spBasket_PurgeAbandoned]
	@OlderThanDays int = 30,
	@Take int = 500
AS
BEGIN
	SET NOCOUNT ON;

	-- Clamped rather than trusted: a negative age would delete the basket a customer is
	-- looking at, and a zero would delete every anonymous basket in the store.
	SET @OlderThanDays = CASE WHEN @OlderThanDays < 1 THEN 1 ELSE @OlderThanDays END;
	SET @Take = CASE WHEN @Take < 1 THEN 1 WHEN @Take > 5000 THEN 5000 ELSE @Take END;

	DELETE TOP (@Take) FROM dbo.Basket
	WHERE [ContactId] IS NULL
	  AND [UpdatedUtc] < DATEADD(DAY, -@OlderThanDays, SYSUTCDATETIME());

	SELECT @@ROWCOUNT AS [Deleted];
END
