-- Sales orders are Purchase rows that carry a Reference (created through the admin portal).
-- Plain POS sales written by the desktop app have a NULL Reference and are excluded here.
CREATE PROCEDURE [dbo].[spOrder_GetAll]
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [p].[Id], [p].[Reference], [p].[AccountId], [a].[Company] AS [AccountName],
	       [p].[Currency], [p].[Status], [p].[PurchaseDate], [p].[SubTotal], [p].[VAT], [p].[FinalPrice],
	       [q].[Reference] AS [FromQuoteReference],
	       COUNT([d].[Id]) AS [Items]
	FROM [dbo].[Purchase] p
	LEFT JOIN [dbo].[Account] a ON a.[Id] = p.[AccountId]
	LEFT JOIN [dbo].[Quote] q ON q.[Id] = p.[QuoteId]
	LEFT JOIN [dbo].[PurchaseDetail] d ON d.[PurchaseId] = p.[Id]
	WHERE [p].[Reference] IS NOT NULL
	GROUP BY [p].[Id], [p].[Reference], [p].[AccountId], [a].[Company], [p].[Currency], [p].[Status],
	         [p].[PurchaseDate], [p].[SubTotal], [p].[VAT], [p].[FinalPrice], [q].[Reference]
	ORDER BY [p].[PurchaseDate] DESC;
END
