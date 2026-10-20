/*
Points Product.ProductImage at a product's first uploaded photograph (T11), or clears it when the
last one has gone.

Only an uploaded image's address is cleared, never a content provider's URL: with no photograph
of the store's own, whatever enrichment found is still better than nothing. The prefix is
ProductImageNames.PathPrefix's.
*/
CREATE PROCEDURE [dbo].[spProductImage_SetPrimary]
	@ProductId int
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @First nvarchar(64) = (
		SELECT TOP (1) [StoredName] FROM dbo.ProductImage
		WHERE [ProductId] = @ProductId
		ORDER BY [SortOrder], [Id]);

	UPDATE dbo.Product
	SET [ProductImage] = CASE
		    WHEN @First IS NOT NULL THEN N'/img/' + @First
		    WHEN [ProductImage] LIKE N'/img/%' THEN NULL
		    ELSE [ProductImage] END
	WHERE [Id] = @ProductId;
END
