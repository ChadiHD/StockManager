-- A product's documents, oldest first. Public like its photographs, so this answers for any
-- product; whether the product is on sale is the caller's to decide, through the catalogue.
CREATE PROCEDURE [dbo].[spProductDocument_GetByProduct]
	@ProductId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [StoredName], [FileName], [ContentType], [SizeBytes]
	FROM dbo.ProductDocument
	WHERE [ProductId] = @ProductId
	ORDER BY [Id];
END
