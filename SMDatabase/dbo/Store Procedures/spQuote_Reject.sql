/*
The customer turning a quote down, with their reason.

A claim, like the accept: one atomic UPDATE whose WHERE and SET share a row lock, so a reject
racing an accept cannot both succeed. A rowcount of zero means somebody already decided it --
which for a company with two buyers is an ordinary Tuesday, not a fault -- and the caller is
told so rather than shown a rejection that did not happen.

The reason is required, for the reason spAccount_Reject requires one: "they said no" is not an
answer to anybody asking what went wrong with the price. Unlike that one it is the customer's
words rather than the store's, so it is quoted to sales and never to a customer.
*/
CREATE PROCEDURE [dbo].[spQuote_Reject]
	@QuoteId int,
	@AccountId int,
	@SiteId int,
	@Reason nvarchar(500)
AS
BEGIN
	SET NOCOUNT ON;

	IF @Reason IS NULL OR LTRIM(RTRIM(@Reason)) = N''
	BEGIN
		THROW 50032, 'A rejection needs a reason.', 1;
	END

	UPDATE dbo.Quote
	SET [Status] = 'Rejected',
	    [RejectedReason] = LTRIM(RTRIM(@Reason))
	WHERE [Id] = @QuoteId
	  AND [AccountId] = @AccountId
	  AND [SiteId] = @SiteId
	  AND [Status] IN ('Requested', 'Priced');

	-- Reported rather than thrown: a quote decided a moment ago by a colleague is the ordinary
	-- case, and the page shows its real state instead of an error.
	SELECT @@ROWCOUNT AS [Rejected];
END
