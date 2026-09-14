CREATE PROCEDURE [dbo].[spOrderLine_GetByOrder]
	@PurchaseId int,
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [d].[Id], [d].[PurchaseId], [d].[ProductId],
	       [p].[Sku], [p].[ProductName] AS [Name],
	       [d].[Quantity], [d].[PurchasePrice] AS [Price], [d].[VAT]
	FROM [dbo].[PurchaseDetail] d
	-- PurchaseDetail is shared with POS receipts and carries no site of its own. Joining the
	-- order gates this on both facts at once: the row is a portal order, and it is this
	-- store's. A guessed id returns nothing rather than another store's line prices.
	INNER JOIN [dbo].[Purchase] o
		ON o.[Id] = d.[PurchaseId]
		AND o.[Reference] IS NOT NULL
		AND o.[SiteId] = @SiteId
	INNER JOIN [dbo].[Product] p ON p.[Id] = d.[ProductId]
	WHERE [d].[PurchaseId] = @PurchaseId
	ORDER BY [d].[Id];
END
