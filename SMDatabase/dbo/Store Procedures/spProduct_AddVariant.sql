/*
Adds one variant to a style (T11): its own SKU, stock and option values, and everything else
the style's.

The style's columns are copied rather than referenced, so every procedure that reads a product
reads a variant without knowing it is one; spProduct_Update keeps the copy in step. The new row
is named after its style and its options ("Linen shirt — M / Navy"), so the basket, quote, order
and mail say which size was bought.

What only the data can refuse is refused here, as sentences for the admin:

  50120  the style is not one this store may sell
  50121  the style is a variant itself, or a feed's product, whose next sync would overwrite it
  50122  no options, a blank one, one name given twice, or a name with a colon in it
  50123  a variant of this style already has exactly these options

The SKU's uniqueness among the store's products is the caller's check, as it is for
spProduct_Insert.
*/
CREATE PROCEDURE [dbo].[spProduct_AddVariant]
	@SiteId int,
	@ParentProductId int,
	@Sku nvarchar(50),
	@QuantityInStock int,
	@Options dbo.ProductOptionList READONLY
AS
BEGIN
	SET NOCOUNT ON;

	IF NOT EXISTS (
		SELECT 1 FROM dbo.Product p
		CROSS APPLY dbo.fnSite_ProductSellable(@SiteId, p.[FeedId], p.[Source], p.[CurrencyCode], p.[PricesIncludeTax]) sel
		WHERE p.[Id] = @ParentProductId AND sel.[Sellable] = 1)
	BEGIN
		THROW 50120, 'That product is not one this store sells.', 1;
	END

	IF EXISTS (
		SELECT 1 FROM dbo.Product
		WHERE [Id] = @ParentProductId
		  AND ([ParentProductId] IS NOT NULL OR [FeedId] IS NOT NULL OR ISNULL([Source], N'Own') = N'Distributor'))
	BEGIN
		THROW 50121, 'Only a product of the store''s own stock, which is not itself a variant, can have variants.', 1;
	END

	IF NOT EXISTS (SELECT 1 FROM @Options)
	   OR EXISTS (SELECT 1 FROM @Options WHERE LTRIM(RTRIM([Name])) = N'' OR LTRIM(RTRIM([Value])) = N'')
	   -- A colon separates name from value in the catalogue's ?opt=Size:M, so a name may not hold one.
	   OR EXISTS (SELECT 1 FROM @Options WHERE CHARINDEX(N':', [Name]) > 0)
	   OR (SELECT COUNT(*) FROM @Options) <> (SELECT COUNT(DISTINCT [Name]) FROM @Options)
	BEGIN
		THROW 50122, 'A variant needs at least one option, each with a name and a single value, such as Size: M.', 1;
	END

	-- Same names, same values, same count: a sibling a customer could not tell apart.
	IF EXISTS (
		SELECT 1 FROM dbo.Product v
		WHERE v.[ParentProductId] = @ParentProductId
		  AND (SELECT COUNT(*) FROM dbo.ProductOption o WHERE o.[ProductId] = v.[Id]) = (SELECT COUNT(*) FROM @Options)
		  AND NOT EXISTS (
		      SELECT 1 FROM @Options n
		      WHERE NOT EXISTS (SELECT 1 FROM dbo.ProductOption o
		                        WHERE o.[ProductId] = v.[Id] AND o.[Name] = n.[Name] AND o.[Value] = n.[Value])))
	BEGIN
		THROW 50123, 'This product already has a variant with exactly those options.', 1;
	END

	DECLARE @Id int;

	BEGIN TRY
		BEGIN TRANSACTION;

		INSERT INTO dbo.Product([ProductName], [Description], [RetailPrice], [QuantityInStock], [IsTaxable],
		                        [Sku], [Category], [Cost], [Source], [Manufacturer], [CurrencyCode],
		                        [PricesIncludeTax], [ParentProductId])
		SELECT [ProductName], [Description], [RetailPrice],
		       CASE WHEN @QuantityInStock < 0 THEN 0 ELSE @QuantityInStock END,
		       [IsTaxable], LTRIM(RTRIM(@Sku)), [Category], [Cost], [Source], [Manufacturer], [CurrencyCode],
		       [PricesIncludeTax], [Id]
		FROM dbo.Product
		WHERE [Id] = @ParentProductId;

		SET @Id = SCOPE_IDENTITY();

		INSERT INTO dbo.ProductOption([ProductId], [Name], [Value], [SortOrder])
		SELECT @Id, LTRIM(RTRIM([Name])), LTRIM(RTRIM([Value])), [SortOrder]
		FROM @Options;

		UPDATE v
		SET [ProductName] = LEFT(s.[ProductName] + N' — ' + lbl.[Label], 100)
		FROM dbo.Product v
		INNER JOIN dbo.Product s ON s.[Id] = v.[ParentProductId]
		INNER JOIN dbo.fnProduct_VariantLabels() lbl ON lbl.[ProductId] = v.[Id]
		WHERE v.[Id] = @Id;

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		THROW;
	END CATCH

	SELECT @Id AS [Id];
END
