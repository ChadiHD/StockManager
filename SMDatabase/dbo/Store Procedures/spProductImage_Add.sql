/*
Records a photograph whose bytes are already stored (T11), last in the product's order.

The caller writes the blob first and this second, and deletes the blob if this fails: a row
naming no bytes is a broken image on a public page, and bytes no row names are only storage.

When it is the product's first photograph, it becomes Product.ProductImage, which every listing
and card already renders. The prefix is ProductImageNames.PathPrefix's, and the two must agree.
A content provider's URL already there is replaced: the store's own photograph is the better one.
*/
CREATE PROCEDURE [dbo].[spProductImage_Add]
	@ProductId int,
	@StoredName nvarchar(64),
	@ContentType nvarchar(50)
AS
BEGIN
	SET NOCOUNT ON;

	BEGIN TRY
		BEGIN TRANSACTION;

		INSERT INTO dbo.ProductImage([ProductId], [StoredName], [ContentType], [SortOrder])
		SELECT @ProductId, @StoredName, @ContentType,
		       ISNULL(MAX([SortOrder]) + 1, 0)
		FROM dbo.ProductImage WITH (UPDLOCK, HOLDLOCK)
		WHERE [ProductId] = @ProductId;

		EXEC dbo.spProductImage_SetPrimary @ProductId = @ProductId;

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		THROW;
	END CATCH
END
