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

	UPDATE dbo.Quote
	SET [Status] = 'Priced'
	WHERE [Id] = @QuoteId
	  AND [SiteId] = @SiteId
	  AND [Status] IN ('Requested', 'Priced');

	SELECT @@ROWCOUNT AS [Priced];
END
