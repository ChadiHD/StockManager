-- Rows for /admin/reports. Covers sales orders raised through the portal; POS sales
-- (Reference IS NULL) are reported by spPurchase_PurchaseReport instead.
CREATE PROCEDURE [dbo].[spReport_GetSales]
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [p].[PurchaseDate] AS [Date],
	       ISNULL([a].[Company], N'—') AS [Account],
	       [p].[Reference] AS [Ref],
	       ISNULL([p].[Currency], N'EUR') AS [Currency],
	       [p].[SubTotal] AS [Net],
	       [p].[VAT] AS [Vat],
	       [p].[FinalPrice] AS [Total]
	FROM [dbo].[Purchase] p
	LEFT JOIN [dbo].[Account] a ON a.[Id] = p.[AccountId] AND a.[SiteId] = @SiteId
	WHERE [p].[Reference] IS NOT NULL
	  AND [p].[SiteId] = @SiteId
	ORDER BY [p].[PurchaseDate] DESC;
END
