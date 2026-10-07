CREATE PROCEDURE [dbo].[spProduct_GetAll]
	-- The desktop till's currency (Pos:CurrencyCode in StockApi, T9). The till has no store, and
	-- before this it listed every product: a GBP feed would have put pound prices on a euro till.
	-- NULL lists everything, for a caller that genuinely wants that.
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
	WHERE @CurrencyCode IS NULL OR [CurrencyCode] = @CurrencyCode
	ORDER BY [ProductName];
END
