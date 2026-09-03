-- Records the outcome of an image lookup. ImageLookupUtc is always stamped, even when nothing
-- was found, so the retry window in spProduct_GetImageCandidates can exclude it next time.
CREATE PROCEDURE [dbo].[spProduct_SetImage]
	@Id int,
	@ProductImage nvarchar(500) = NULL
AS
BEGIN
	SET NOCOUNT ON;

	UPDATE dbo.Product
	SET [ProductImage] = CASE
	                       WHEN @ProductImage IS NULL OR LTRIM(RTRIM(@ProductImage)) = N''
	                       THEN [ProductImage]
	                       ELSE @ProductImage
	                     END,
	    [ImageSourcedUtc] = CASE
	                          WHEN @ProductImage IS NULL OR LTRIM(RTRIM(@ProductImage)) = N''
	                          THEN [ImageSourcedUtc]
	                          ELSE SYSUTCDATETIME()
	                        END,
	    [ImageLookupUtc] = SYSUTCDATETIME(),
	    [LastModified] = SYSUTCDATETIME()
	WHERE [Id] = @Id;
END
