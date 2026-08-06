CREATE PROCEDURE [dbo].[spOrderLine_GetByOrder]
	@PurchaseId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [d].[Id], [d].[PurchaseId], [d].[ProductId],
	       [p].[Sku], [p].[ProductName] AS [Name],
	       [d].[Quantity], [d].[PurchasePrice] AS [Price], [d].[VAT]
	FROM [dbo].[PurchaseDetail] d
	INNER JOIN [dbo].[Product] p ON p.[Id] = d.[ProductId]
	WHERE [d].[PurchaseId] = @PurchaseId
	ORDER BY [d].[Id];
END
