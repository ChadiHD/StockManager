-- Products that still need an image, ordered so the most promising are tried first: rows the
-- feed flagged as having Icecat content, then rows with an EAN, then the rest.
--
-- Only rows with something to look up are returned, and anything tried within the retry window
-- is skipped so a product with no Icecat match is not requested on every pass.
CREATE PROCEDURE [dbo].[spProduct_GetImageCandidates]
	@Take int = 100,
	@RetryAfterHours int = 168
AS
BEGIN
	SET NOCOUNT ON;

	SELECT TOP (@Take)
	       [Id], [Sku], [ProductName], [Manufacturer], [ManufacturerPartNumber], [Ean], [IcecatAvailable]
	FROM [dbo].[Product]
	WHERE ([ProductImage] IS NULL OR LTRIM(RTRIM([ProductImage])) = N'')
	  AND [Delisted] = 0
	  AND (
	        ([Manufacturer] IS NOT NULL AND [ManufacturerPartNumber] IS NOT NULL)
	     OR ([Ean] IS NOT NULL AND LTRIM(RTRIM([Ean])) <> N'')
	      )
	  AND ([ImageLookupUtc] IS NULL
	       OR [ImageLookupUtc] < DATEADD(HOUR, -@RetryAfterHours, SYSUTCDATETIME()))
	ORDER BY
	       CASE WHEN [IcecatAvailable] = 1 THEN 0 ELSE 1 END,
	       CASE WHEN [Ean] IS NOT NULL AND LTRIM(RTRIM([Ean])) <> N'' THEN 0 ELSE 1 END,
	       [Id];
END
