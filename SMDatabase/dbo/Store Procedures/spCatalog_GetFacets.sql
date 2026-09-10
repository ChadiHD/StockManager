/*
Facet counts for the catalog rail, under the same visibility rules as spCatalog_Search.

Counts are computed against the current filter minus the facet's own dimension, so the
category counts show what selecting each category would give and are not all reduced to the
already-selected one. Availability is the exception: it is a narrowing toggle, so its count
reflects the filter as it stands.

The visible set goes into a temp table rather than a CTE. A CTE is in scope only for the
statement immediately after it, and this procedure returns two grids — categories, then
brands — so a CTE gives "Invalid object name" on the second.
*/
CREATE PROCEDURE [dbo].[spCatalog_GetFacets]
	@SiteId int,
	@CustomerGroupId int = NULL,
	@CategorySlug nvarchar(80) = NULL,
	@Brand nvarchar(100) = NULL,
	@InStockOnly bit = 0,
	@Search nvarchar(200) = NULL
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @Term nvarchar(200) = NULLIF(LTRIM(RTRIM(@Search)), N'');

	DECLARE @Escaped nvarchar(400) =
		REPLACE(REPLACE(REPLACE(REPLACE(@Term, N'\', N'\\'), N'%', N'\%'), N'_', N'\_'), N'[', N'\[');

	DECLARE @Prefix nvarchar(402) = @Escaped + N'%';
	DECLARE @Contains nvarchar(404) = N'%' + @Escaped + N'%';

	DECLARE @HasIncludeRule bit =
		CASE WHEN @CustomerGroupId IS NULL THEN 0
		     WHEN EXISTS (SELECT 1 FROM dbo.GroupVisibility
		                  WHERE [CustomerGroupId] = @CustomerGroupId AND [Rule] = 'IncludeCategory')
		     THEN 1 ELSE 0 END;

	CREATE TABLE #visible
	(
		[Manufacturer] NVARCHAR(100) NULL,
		[CategorySlug] NVARCHAR(80) NOT NULL,
		[CategoryName] NVARCHAR(120) NOT NULL,
		[SortOrder] INT NOT NULL
	);

	INSERT INTO #visible ([Manufacturer], [CategorySlug], [CategoryName], [SortOrder])
	SELECT [p].[Manufacturer], [c].[Slug], [c].[Name], [c].[SortOrder]
	FROM [dbo].[Product] p
	INNER JOIN [dbo].[CategoryMapping] m
		ON m.[SiteId] = @SiteId
		AND m.[FeedValue] = p.[Category]
	INNER JOIN [dbo].[SiteCategory] c
		ON c.[Id] = m.[SiteCategoryId]
		AND c.[IsActive] = 1
	WHERE p.[Published] = 1
	  AND p.[Delisted] = 0
	  AND (@InStockOnly = 0 OR p.[QuantityInStock] > 0)
	  AND (@Term IS NULL
	       OR p.[Sku] = @Term
	       OR p.[ManufacturerPartNumber] = @Term
	       OR p.[Sku] LIKE @Prefix ESCAPE N'\'
	       OR p.[ProductName] LIKE @Contains ESCAPE N'\'
	       OR p.[ManufacturerPartNumber] LIKE @Contains ESCAPE N'\'
	       OR p.[Description] LIKE @Contains ESCAPE N'\')
	  AND (@HasIncludeRule = 0 OR EXISTS (
	          SELECT 1 FROM dbo.GroupVisibility g
	          WHERE g.[CustomerGroupId] = @CustomerGroupId
	            AND g.[Rule] = 'IncludeCategory'
	            AND g.[Value] = c.[Slug]))
	  AND NOT EXISTS (
	          SELECT 1 FROM dbo.GroupVisibility g
	          WHERE g.[CustomerGroupId] = @CustomerGroupId
	            AND g.[Rule] = 'ExcludeCategory'
	            AND g.[Value] = c.[Slug])
	  AND NOT EXISTS (
	          SELECT 1 FROM dbo.GroupVisibility g
	          WHERE g.[CustomerGroupId] = @CustomerGroupId
	            AND g.[Rule] = 'ExcludeDistributor'
	            AND g.[Value] = p.[Distributor]);

	-- Categories, counted without the category filter applied.
	SELECT [CategorySlug] AS [Slug], [CategoryName] AS [Name], COUNT(*) AS [Count]
	FROM #visible
	WHERE (@Brand IS NULL OR [Manufacturer] = @Brand)
	GROUP BY [CategorySlug], [CategoryName], [SortOrder]
	ORDER BY [SortOrder], [CategoryName];

	-- Brands, counted without the brand filter applied. Manufacturer is NULL across an entire
	-- feed when its field mapping has no manufacturer column, so the facet is empty rather
	-- than wrong in that case — a mapping problem, visible as a missing rail section.
	SELECT [Manufacturer] AS [Name], COUNT(*) AS [Count]
	FROM #visible
	WHERE [Manufacturer] IS NOT NULL
	  AND (@CategorySlug IS NULL OR [CategorySlug] = @CategorySlug)
	GROUP BY [Manufacturer]
	ORDER BY COUNT(*) DESC, [Manufacturer];

	DROP TABLE #visible;
END
