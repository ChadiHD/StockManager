-- The order a quote became, if it became one.
--
-- Exact rather than a guess, which is the point: UQ_Purchase_QuoteId makes one order per quote
-- a database fact, so a caller that has just converted a quote can read back the order it
-- created instead of taking the store's newest one. Under two concurrent conversions of two
-- different quotes, "newest" is the other caller's order.
CREATE PROCEDURE [dbo].[spOrder_GetByQuote]
	@QuoteId int,
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
	LEFT JOIN [dbo].[Account] a ON a.[Id] = p.[AccountId] AND a.[SiteId] = @SiteId
	LEFT JOIN [dbo].[Quote] q ON q.[Id] = p.[QuoteId] AND q.[SiteId] = @SiteId
	LEFT JOIN [dbo].[PurchaseDetail] d ON d.[PurchaseId] = p.[Id]
	WHERE [p].[QuoteId] = @QuoteId
	  -- Both filters, independently: the Reference predicate keeps POS receipts out and the
	  -- site predicate keeps other stores' orders out. Neither implies the other.
	  AND [p].[Reference] IS NOT NULL
	  AND [p].[SiteId] = @SiteId
	GROUP BY [p].[Id], [p].[Reference], [p].[AccountId], [a].[Company], [p].[Currency], [p].[Status],
	         [p].[PurchaseDate], [p].[SubTotal], [p].[VAT], [p].[FinalPrice], [q].[Reference],
	         [p].[PoNumber];
END
