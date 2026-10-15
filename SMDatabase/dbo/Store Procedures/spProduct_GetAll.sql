CREATE PROCEDURE [dbo].[spProduct_GetAll]
	-- The desktop till's currency (Pos:CurrencyCode in StockApi, T9). The till has no store, and
	-- before this it listed every product: a GBP feed would have put pound prices on a euro till.
	-- NULL lists every currency, for a caller that genuinely wants that.
	@CurrencyCode nvarchar(3) = NULL
AS
BEGIN
	SET NOCOUNT ON;

	-- The trailing catalog columns are additive: Dapper ignores columns a model does not
	-- declare, so the desktop POS's ProductModel keeps binding exactly as before.
	SELECT [Id], [ProductName], [Description], [RetailPrice], [QuantityInStock], [IsTaxable], [ProductImage],
	       [Sku], [Category], [Cost], [Source], [Distributor], [DistributorSku], [LastSynced], [Delisted],
	       [Manufacturer], [ManufacturerPartNumber], [Ean], [IcecatAvailable], [ImageSourcedUtc]
	FROM [dbo].[Product]
	-- Net prices only (T10): the till adds tax on top, so a price that already includes it would
	-- be taxed twice.
	WHERE (@CurrencyCode IS NULL OR [CurrencyCode] = @CurrencyCode)
	  AND [PricesIncludeTax] = 0
	ORDER BY [ProductName];
END
