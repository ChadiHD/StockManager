/*
Forgets one photograph of a product (T11) and says whether it did, so the caller deletes the
bytes only of an image that was this product's. The next one becomes the primary.
*/
CREATE PROCEDURE [dbo].[spProductImage_Remove]
	@ProductId int,
	@StoredName nvarchar(64)
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @Removed int;

	BEGIN TRY
		BEGIN TRANSACTION;

		DELETE FROM dbo.ProductImage
		WHERE [ProductId] = @ProductId AND [StoredName] = @StoredName;

		SET @Removed = @@ROWCOUNT;

		EXEC dbo.spProductImage_SetPrimary @ProductId = @ProductId;

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		THROW;
	END CATCH

	SELECT @Removed AS [Removed];
END
