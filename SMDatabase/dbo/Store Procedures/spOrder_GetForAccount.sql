-- One order, by reference, for the account it belongs to. See spQuote_GetForAccount: a
-- reference is not an authorisation, and SO-0012 is as guessable as QT-0041.
CREATE PROCEDURE [dbo].[spOrder_GetForAccount]
	@Reference nvarchar(20),
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
	WHERE [p].[Reference] = @Reference
	  AND [p].[AccountId] = @AccountId
	  AND [p].[SiteId] = @SiteId
	GROUP BY [p].[Id], [p].[Reference], [p].[AccountId], [a].[Company], [p].[Currency], [p].[Status],
	         [p].[PurchaseDate], [p].[SubTotal], [p].[VAT], [p].[FinalPrice], [q].[Reference],
	         [p].[PoNumber];
END
