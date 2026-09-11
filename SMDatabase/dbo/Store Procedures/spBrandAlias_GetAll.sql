-- Every brand alias. The table is tiny and read on every enrichment batch, so it is fetched
-- whole and cached in the resolver rather than queried per product.
CREATE PROCEDURE [dbo].[spBrandAlias_GetAll]
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [Id], [Distributor], [FeedBrand], [IcecatBrand], [Note], [CreatedDate]
	FROM [dbo].[BrandAlias]
	ORDER BY [FeedBrand], [Distributor];
END
