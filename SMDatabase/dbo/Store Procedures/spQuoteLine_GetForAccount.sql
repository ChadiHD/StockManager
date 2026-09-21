/*
A quote's lines, for the account that raised it.

The account predicate is repeated here rather than left to the caller having resolved the quote
through spQuote_GetForAccount first. A quote id is sequential, so a caller that skipped that
step would read another customer's lines and prices -- and defence that depends on call order is
defence that survives until somebody adds a second caller.

No Delisted predicate and no staleness predicate, deliberately, exactly as
spQuoteLine_GetByQuote has none: a product the distributor dropped still belongs on the quote
that already contains it, and hiding stock from the shop window is not withdrawing a price
already quoted. DelistedProductHistoryTests is what holds that for the admin read; the same
reasoning applies here and for the same reason.
*/
CREATE PROCEDURE [dbo].[spQuoteLine_GetForAccount]
	@QuoteId int,
	@AccountId int,
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [l].[Id], [l].[QuoteId], [l].[ProductId],
	       [p].[Sku], [p].[ProductName] AS [Name],
	       [l].[Quantity], [l].[ListPrice], [l].[DiscountPct], [l].[NetPrice]
	FROM [dbo].[QuoteLine] l
	INNER JOIN [dbo].[Quote] q
		ON q.[Id] = l.[QuoteId]
		AND q.[AccountId] = @AccountId
		AND q.[SiteId] = @SiteId
	INNER JOIN [dbo].[Product] p ON p.[Id] = l.[ProductId]
	WHERE [l].[QuoteId] = @QuoteId
	ORDER BY [l].[Id];
END
