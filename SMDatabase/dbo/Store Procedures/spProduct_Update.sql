/*
Saves a product from the admin's product screens. Every column it is given is written, so the
portal sends back what it does not edit.

Since T11 two things depend on what the row is:

  - A variant writes its stock and nothing else. Its name, price, cost, category and the rest
    are its style's, and a variant priced apart from its style would show the listing one price
    and charge another.
  - A style passes what a variant inherits on to its variants, in the same statement batch, so
    a renamed or repriced shirt is renamed and repriced in every size.

@Manufacturer arrived with T11 too: the store's own brand for its own stock. A feed's sync
overwrites it on a feed product, which is why the portal offers it on own stock only.
*/
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
	@ProductImage nvarchar(500),
	@Manufacturer nvarchar(100)
AS
BEGIN
	SET NOCOUNT ON;

	IF EXISTS (SELECT 1 FROM dbo.Product WHERE [Id] = @Id AND [ParentProductId] IS NOT NULL)
	BEGIN
		UPDATE dbo.Product
		SET [QuantityInStock] = @QuantityInStock,
		    [LastModified] = SYSUTCDATETIME()
		WHERE [Id] = @Id;

		RETURN;
	END

	BEGIN TRY
		BEGIN TRANSACTION;

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
		    [Manufacturer] = NULLIF(LTRIM(RTRIM(@Manufacturer)), N''),
		    [LastModified] = SYSUTCDATETIME()
		WHERE [Id] = @Id;

		-- The columns spProduct_AddVariant copied. Not stock, which is each variant's own, and not
		-- the image, which the style's page shows for every size.
		UPDATE v
		SET [ProductName] = LEFT(@ProductName + N' — ' + ISNULL(lbl.[Label], N''), 100),
		    [Description] = @Description,
		    [Category] = @Category,
		    [Source] = @Source,
		    [Cost] = @Cost,
		    [RetailPrice] = @RetailPrice,
		    [IsTaxable] = @IsTaxable,
		    [Manufacturer] = NULLIF(LTRIM(RTRIM(@Manufacturer)), N''),
		    [LastModified] = SYSUTCDATETIME()
		FROM dbo.Product v
		LEFT JOIN dbo.fnProduct_VariantLabels() lbl ON lbl.[ProductId] = v.[Id]
		WHERE v.[ParentProductId] = @Id;

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		THROW;
	END CATCH
END
