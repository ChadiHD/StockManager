CREATE PROCEDURE [dbo].[spProduct_Insert]
	@Id int output,
	@Sku nvarchar(50),
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

	INSERT INTO dbo.Product([ProductName], [Description], [RetailPrice], [QuantityInStock], [IsTaxable],
	                        [ProductImage], [Sku], [Category], [Cost], [Source], [Distributor], [DistributorSku])
	VALUES (@ProductName, @Description, @RetailPrice, @QuantityInStock, @IsTaxable,
	        @ProductImage, @Sku, @Category, @Cost, @Source, @Distributor, @DistributorSku);

	SELECT @Id = SCOPE_IDENTITY();
END
