CREATE PROCEDURE [dbo].[spProduct_GetBySku]
	@Sku nvarchar(50)
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [Id], [ProductName], [Description], [RetailPrice], [QuantityInStock], [IsTaxable], [ProductImage],
	       [Sku], [Category], [Cost], [Source], [Distributor], [DistributorSku], [LastSynced], [Delisted],
	       [Manufacturer], [ManufacturerPartNumber], [Ean], [IcecatAvailable], [ImageSourcedUtc]
	FROM [dbo].[Product]
	WHERE [Sku] = @Sku;
END
