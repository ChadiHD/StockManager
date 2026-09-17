CREATE PROCEDURE [dbo].[spQuoteLine_GetByQuote]
	@QuoteId int,
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [l].[Id], [l].[QuoteId], [l].[ProductId],
	       [p].[Sku], [p].[ProductName] AS [Name],
	       [l].[Quantity], [l].[ListPrice], [l].[DiscountPct], [l].[NetPrice]
	FROM [dbo].[QuoteLine] l
	-- QuoteLine carries no site of its own; it inherits the quote's. Joining rather than
	-- trusting the caller to have looked the quote up first means a guessed quote id returns
	-- an empty list instead of another store's lines and prices.
	INNER JOIN [dbo].[Quote] q
		ON q.[Id] = l.[QuoteId]
		AND q.[SiteId] = @SiteId
	INNER JOIN [dbo].[Product] p ON p.[Id] = l.[ProductId]
	WHERE [l].[QuoteId] = @QuoteId
	ORDER BY [l].[Id];
END
