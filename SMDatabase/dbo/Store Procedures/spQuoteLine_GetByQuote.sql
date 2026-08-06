CREATE PROCEDURE [dbo].[spQuoteLine_GetByQuote]
	@QuoteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [l].[Id], [l].[QuoteId], [l].[ProductId],
	       [p].[Sku], [p].[ProductName] AS [Name],
	       [l].[Quantity], [l].[ListPrice], [l].[DiscountPct], [l].[NetPrice]
	FROM [dbo].[QuoteLine] l
	INNER JOIN [dbo].[Product] p ON p.[Id] = l.[ProductId]
	WHERE [l].[QuoteId] = @QuoteId
	ORDER BY [l].[Id];
END
