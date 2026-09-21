-- An order's lines, for the account it belongs to. See spQuoteLine_GetForAccount for why the
-- predicate is repeated rather than left to the caller having resolved the order first.
--
-- PurchaseDetail is shared with POS receipts and carries no site of its own, so the join gates
-- on three facts at once: the row is a portal order, it is this store's, and it is this
-- customer's.
CREATE PROCEDURE [dbo].[spOrderLine_GetForAccount]
	@PurchaseId int,
	@AccountId int,
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [d].[Id], [d].[PurchaseId], [d].[ProductId],
	       [p].[Sku], [p].[ProductName] AS [Name],
	       [d].[Quantity], [d].[PurchasePrice] AS [Price], [d].[VAT]
	FROM [dbo].[PurchaseDetail] d
	INNER JOIN [dbo].[Purchase] o
		ON o.[Id] = d.[PurchaseId]
		AND o.[Reference] IS NOT NULL
		AND o.[AccountId] = @AccountId
		AND o.[SiteId] = @SiteId
	INNER JOIN [dbo].[Product] p ON p.[Id] = d.[ProductId]
	WHERE [d].[PurchaseId] = @PurchaseId
	ORDER BY [d].[Id];
END
