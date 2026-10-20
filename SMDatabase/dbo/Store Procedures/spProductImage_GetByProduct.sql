-- A product's photographs in the order its page shows them (T11). Photographs are public, so
-- this answers for any product; deciding whether the product is on sale is the caller's, through
-- the catalogue's own procedures.
CREATE PROCEDURE [dbo].[spProductImage_GetByProduct]
	@ProductId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [StoredName]
	FROM dbo.ProductImage
	WHERE [ProductId] = @ProductId
	ORDER BY [SortOrder], [Id];
END
