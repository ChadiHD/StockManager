-- Sales orders are Purchase rows that carry a Reference (created through the admin portal).
-- Plain POS sales written by the desktop app have a NULL Reference and are excluded here.
CREATE PROCEDURE [dbo].[spOrder_GetAll]
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [p].[Id], [p].[Reference], [p].[AccountId], [a].[Company] AS [AccountName],
	       [p].[Currency], [p].[Status], [p].[PurchaseDate], [p].[SubTotal], [p].[VAT], [p].[FinalPrice],
	       [q].[Reference] AS [FromQuoteReference],
	       COUNT([d].[Id]) AS [Items]
	FROM [dbo].[Purchase] p
	LEFT JOIN [dbo].[Account] a ON a.[Id] = p.[AccountId] AND a.[SiteId] = @SiteId
	LEFT JOIN [dbo].[Quote] q ON q.[Id] = p.[QuoteId] AND q.[SiteId] = @SiteId
	LEFT JOIN [dbo].[PurchaseDetail] d ON d.[PurchaseId] = p.[Id]
	-- Both predicates matter and neither implies the other: Reference separates portal orders
	-- from POS sales, SiteId separates one store's orders from another's.
	WHERE [p].[Reference] IS NOT NULL
	  AND [p].[SiteId] = @SiteId
	GROUP BY [p].[Id], [p].[Reference], [p].[AccountId], [a].[Company], [p].[Currency], [p].[Status],
	         [p].[PurchaseDate], [p].[SubTotal], [p].[VAT], [p].[FinalPrice], [q].[Reference]
	ORDER BY [p].[PurchaseDate] DESC;
END
