/*
Removes a variant nobody holds (T11).

Refused while a basket, a quote or an order has it, because those lines are a customer's, and a
line whose product vanished would be rewritten under them. The admin sets its stock to zero
instead, which takes it off sale without touching anybody's line.

  50124  not a variant of a product this store sells
  50125  a basket, quote or order holds it
*/
CREATE PROCEDURE [dbo].[spProduct_RemoveVariant]
	@SiteId int,
	@Id int
AS
BEGIN
	SET NOCOUNT ON;

	IF NOT EXISTS (
		SELECT 1 FROM dbo.Product v
		INNER JOIN dbo.Product s ON s.[Id] = v.[ParentProductId]
		CROSS APPLY dbo.fnSite_ProductSellable(@SiteId, s.[FeedId], s.[Source], s.[CurrencyCode], s.[PricesIncludeTax]) sel
		WHERE v.[Id] = @Id AND sel.[Sellable] = 1)
	BEGIN
		THROW 50124, 'That is not a variant of a product this store sells.', 1;
	END

	IF EXISTS (SELECT 1 FROM dbo.BasketLine WHERE [ProductId] = @Id)
	   OR EXISTS (SELECT 1 FROM dbo.QuoteLine WHERE [ProductId] = @Id)
	   OR EXISTS (SELECT 1 FROM dbo.PurchaseDetail WHERE [ProductId] = @Id)
	   OR EXISTS (SELECT 1 FROM dbo.Inventory WHERE [ProductId] = @Id)
	BEGIN
		THROW 50125, 'A basket, quote or order holds this variant. Set its stock to zero to take it off sale instead.', 1;
	END

	BEGIN TRY
		BEGIN TRANSACTION;

		DELETE FROM dbo.ProductOption WHERE [ProductId] = @Id;
		DELETE FROM dbo.SiteProduct WHERE [ProductId] = @Id;
		DELETE FROM dbo.Product WHERE [Id] = @Id;

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		THROW;
	END CATCH
END
