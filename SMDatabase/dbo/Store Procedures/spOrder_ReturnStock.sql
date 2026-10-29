-- Puts back the own stock a cart order took (T12, T13). One definition for both ways an order is
-- cancelled: by an admin in spOrder_UpdateStatus, and by a failed or expired payment in
-- spOrder_ApplyPaymentEvent. The caller has already claimed the cancellation, so this runs once.
CREATE PROCEDURE [dbo].[spOrder_ReturnStock]
	@PurchaseId int
AS
BEGIN
	SET NOCOUNT ON;

	-- Own stock only, as spOrder_PlaceFromBasket took it.
	UPDATE p
	SET [QuantityInStock] = p.[QuantityInStock] + d.[Quantity]
	FROM dbo.Product p
	INNER JOIN (SELECT [ProductId], SUM([Quantity]) AS [Quantity]
	            FROM dbo.PurchaseDetail
	            WHERE [PurchaseId] = @PurchaseId
	            GROUP BY [ProductId]) d ON d.[ProductId] = p.[Id]
	WHERE p.[FeedId] IS NULL
	  AND ISNULL(p.[Source], N'Own') <> N'Distributor';
END
