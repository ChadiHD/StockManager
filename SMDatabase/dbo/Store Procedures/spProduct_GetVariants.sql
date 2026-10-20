/*
A style's variants for the admin's product page (T11): each one's SKU, label and stock, in the
order they were added.

Fetched when the page opens rather than carried in the products snapshot, because the snapshot
holds every product and the joins this needs cost it more than half a second at 50,000 rows.
Through the acting store's sellable set, like every admin product read since T9.
*/
CREATE PROCEDURE [dbo].[spProduct_GetVariants]
	@SiteId int,
	@ProductId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [v].[Id], [v].[Sku], [lbl].[Label], [v].[QuantityInStock], [v].[Delisted]
	FROM [dbo].[Product] s
	CROSS APPLY [dbo].[fnSite_ProductSellable](@SiteId, [s].[FeedId], [s].[Source], [s].[CurrencyCode], [s].[PricesIncludeTax]) sel
	INNER JOIN [dbo].[Product] v ON [v].[ParentProductId] = [s].[Id]
	LEFT JOIN [dbo].[fnProduct_VariantLabels]() lbl ON [lbl].[ProductId] = [v].[Id]
	WHERE [s].[Id] = @ProductId AND [sel].[Sellable] = 1
	ORDER BY [v].[Id];
END
