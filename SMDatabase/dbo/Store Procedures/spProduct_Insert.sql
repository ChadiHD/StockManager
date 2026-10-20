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
	@CurrencyCode nvarchar(3),
	-- And its tax basis (T10), for the same reason: the price just typed is the one its shelf
	-- shows, with tax in or without.
	@PricesIncludeTax bit,
	-- The store's own brand for its own stock (T11). Until then nothing wrote one, so own stock
	-- showed no brand at all.
	@Manufacturer nvarchar(100)
AS
BEGIN
	SET NOCOUNT ON;

	INSERT INTO dbo.Product([ProductName], [Description], [RetailPrice], [QuantityInStock], [IsTaxable],
	                        [ProductImage], [Sku], [Category], [Cost], [Source], [Distributor], [DistributorSku],
	                        [CurrencyCode], [PricesIncludeTax], [Manufacturer])
	VALUES (@ProductName, @Description, @RetailPrice, @QuantityInStock, @IsTaxable,
	        @ProductImage, @Sku, @Category, @Cost, @Source, @Distributor, @DistributorSku,
	        @CurrencyCode, @PricesIncludeTax, NULLIF(LTRIM(RTRIM(@Manufacturer)), N''));

	SELECT @Id = SCOPE_IDENTITY();
END
