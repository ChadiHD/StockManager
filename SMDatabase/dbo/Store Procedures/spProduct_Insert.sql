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
	@ProductImage nvarchar(500),
	-- The acting store's (T9): the prices just typed are in it, and own stock may be sold by any
	-- store in this currency. No FeedId, so never a feed's, whatever Source says.
	@CurrencyCode nvarchar(3)
AS
BEGIN
	SET NOCOUNT ON;

	INSERT INTO dbo.Product([ProductName], [Description], [RetailPrice], [QuantityInStock], [IsTaxable],
	                        [ProductImage], [Sku], [Category], [Cost], [Source], [Distributor], [DistributorSku],
	                        [CurrencyCode])
	VALUES (@ProductName, @Description, @RetailPrice, @QuantityInStock, @IsTaxable,
	        @ProductImage, @Sku, @Category, @Cost, @Source, @Distributor, @DistributorSku,
	        @CurrencyCode);

	SELECT @Id = SCOPE_IDENTITY();
END
