CREATE PROCEDURE [dbo].[spProduct_GetAll]

AS
BEGIN
	SET NOCOUNT ON;

	-- The trailing catalog columns are additive: Dapper ignores columns a model does not
	-- declare, so the desktop POS's ProductModel keeps binding exactly as before.
	SELECT [Id], [ProductName], [Description], [RetailPrice], [QuantityInStock], [IsTaxable], [ProductImage],
	       [Sku], [Category], [Cost], [Source], [Distributor], [DistributorSku], [LastSynced], [Delisted]
	FROM [dbo].[Product]
	ORDER BY [ProductName];
END
