-- Forgets one of a product's documents and says whether it did, so the caller deletes only the
-- bytes of a document that was this product's.
CREATE PROCEDURE [dbo].[spProductDocument_Remove]
	@ProductId int,
	@StoredName nvarchar(64)
AS
BEGIN
	SET NOCOUNT ON;

	DELETE FROM dbo.ProductDocument
	WHERE [ProductId] = @ProductId AND [StoredName] = @StoredName;

	SELECT @@ROWCOUNT AS [Removed];
END
