-- One account's orders, for the customer's own list. See spQuote_GetByAccount for why this is
-- its own procedure rather than spOrder_GetAll with a parameter.
--
-- Both filters, independently: the Reference predicate keeps POS receipts out and the account
-- and site predicates keep other customers' orders out. Neither implies the other.
CREATE PROCEDURE [dbo].[spOrder_GetByAccount]
	@AccountId int,
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [p].[Id], [p].[Reference], [p].[AccountId], [a].[Company] AS [AccountName],
	       [p].[Currency], [p].[Status], [p].[PurchaseDate], [p].[SubTotal], [p].[VAT], [p].[FinalPrice],
	       [q].[Reference] AS [FromQuoteReference],
	       [p].[PoNumber],
	       COUNT([d].[Id]) AS [Items]
	FROM [dbo].[Purchase] p
	INNER JOIN [dbo].[Account] a ON a.[Id] = p.[AccountId] AND a.[SiteId] = @SiteId
	LEFT JOIN [dbo].[Quote] q ON q.[Id] = p.[QuoteId] AND q.[SiteId] = @SiteId
	LEFT JOIN [dbo].[PurchaseDetail] d ON d.[PurchaseId] = p.[Id]
	WHERE [p].[Reference] IS NOT NULL
	  AND [p].[AccountId] = @AccountId
	  AND [p].[SiteId] = @SiteId
	GROUP BY [p].[Id], [p].[Reference], [p].[AccountId], [a].[Company], [p].[Currency], [p].[Status],
	         [p].[PurchaseDate], [p].[SubTotal], [p].[VAT], [p].[FinalPrice], [q].[Reference],
	         [p].[PoNumber]
	ORDER BY [p].[PurchaseDate] DESC;
END
