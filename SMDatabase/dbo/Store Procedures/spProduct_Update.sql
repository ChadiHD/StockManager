CREATE PROCEDURE [dbo].[spProduct_Update]
	@Id int,
	@ProductName nvarchar(100),
	@Description nvarchar(MAX),
	@Category nvarchar(50),
	@Source nvarchar(20),
	@Distributor nvarchar(100),
	@DistributorSku nvarchar(50),
	@Cost money,
	@RetailPrice money,
	@QuantityInStock int,
	@IsTaxable bit,
	@ProductImage nvarchar(500)
AS
BEGIN
	SET NOCOUNT ON;

	UPDATE dbo.Product
	SET [ProductName] = @ProductName,
	    [Description] = @Description,
	    [Category] = @Category,
	    [Source] = @Source,
	    [Distributor] = @Distributor,
	    [DistributorSku] = @DistributorSku,
	    [Cost] = @Cost,
	    [RetailPrice] = @RetailPrice,
	    [QuantityInStock] = @QuantityInStock,
	    [IsTaxable] = @IsTaxable,
	    [ProductImage] = @ProductImage,
	    [LastModified] = SYSUTCDATETIME()
	WHERE [Id] = @Id;
END
