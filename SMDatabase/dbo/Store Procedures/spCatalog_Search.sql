/*
Paged, faceted, site-scoped product search for the storefront.

Three things are deliberate:

1. Visibility is decided inside the query, not filtered afterwards. A product removed from an
   already-fetched page has still been counted, still shifted the paging, and has usually
   already reached the browser. Every gate below is part of the same predicate.

2. A product belongs to this store when its feed category has a dbo.CategoryMapping row for
   the site — dbo.Product carries no SiteId, because the desktop POS and own stock have no
   site and one product may be sold by two stores. An unmapped feed value is invisible rather
   than uncategorised.

3. Matching is literal, never fuzzy. Sibling identifiers (SKU, MPN, EAN) are the same shape as
   one another, and approximate matching on them returns confidently wrong products — the same
   reason FuzzySearch gives code fields no fuzzy pass.

Full-text was the intended mechanism and is not used: the development SQL Server container
reports IsFullTextInstalled = 0, so CONTAINSTABLE is unavailable there even though Azure SQL
supports it. The ranking below is portable and correct at this catalog's size; swapping it for
CONTAINSTABLE later is a change to this file alone.
*/
CREATE PROCEDURE [dbo].[spCatalog_Search]
	@SiteId int,
	@CustomerGroupId int = NULL,
	@CategorySlug nvarchar(80) = NULL,
	@Brand nvarchar(100) = NULL,
	@InStockOnly bit = 0,
	@Search nvarchar(200) = NULL,
	-- 'featured' | 'price-asc' | 'price-desc' | 'name'
	@Sort nvarchar(20) = NULL,
	@Page int = 1,
	@PageSize int = 24
AS
BEGIN
	SET NOCOUNT ON;

	-- Caps the page size so a crafted query cannot ask for the whole catalog in one response.
	SET @PageSize = CASE WHEN @PageSize NOT BETWEEN 1 AND 96 THEN 24 ELSE @PageSize END;

	-- Bounded at both ends. The ceiling is not tidiness: (@Page - 1) * @PageSize is int
	-- arithmetic, so at the largest allowed page size a page number in the tens of millions
	-- overflows and the request fails with "Arithmetic overflow error converting expression to
	-- data type int" — an unauthenticated 500 from one query string. Past the real last page
	-- the result is simply empty, which the caller handles.
	SET @Page = CASE WHEN ISNULL(@Page, 1) < 1 THEN 1
	                 WHEN @Page > 100000 THEN 100000
	                 ELSE @Page END;

	-- Anything the sort control did not send falls back to the default instead of matching no
	-- CASE branch and silently landing in an arbitrary order.
	SET @Sort = CASE WHEN @Sort IN (N'price-asc', N'price-desc', N'name', N'featured')
	                 THEN @Sort ELSE N'featured' END;

	DECLARE @Term nvarchar(200) = NULLIF(LTRIM(RTRIM(@Search)), N'');

	-- Wildcards and the escape character itself are escaped, so a search for "50%" or "A_B"
	-- looks for that text instead of matching everything.
	DECLARE @Escaped nvarchar(400) =
		REPLACE(REPLACE(REPLACE(REPLACE(@Term, N'\', N'\\'), N'%', N'\%'), N'_', N'\_'), N'[', N'\[');

	DECLARE @Prefix nvarchar(402) = @Escaped + N'%';
	DECLARE @Contains nvarchar(404) = N'%' + @Escaped + N'%';

	-- An IncludeCategory rule turns the group's visibility into an allow-list; without one,
	-- everything is visible except what the exclusions remove.
	DECLARE @HasIncludeRule bit =
		CASE WHEN @CustomerGroupId IS NULL THEN 0
		     WHEN EXISTS (SELECT 1 FROM dbo.GroupVisibility
		                  WHERE [CustomerGroupId] = @CustomerGroupId AND [Rule] = 'IncludeCategory')
		     THEN 1 ELSE 0 END;

	WITH [visible] AS
	(
		SELECT
			[p].[Id],
			[p].[Sku],
			[p].[ProductName],
			[p].[Description],
			[p].[RetailPrice],
			[p].[Cost],
			[p].[QuantityInStock],
			[p].[ProductImage],
			[p].[Manufacturer],
			[p].[ManufacturerPartNumber],
			[p].[Distributor],
			[p].[Source],
			[p].[Badge],
			[p].[Featured],
			[c].[Slug] AS [CategorySlug],
			[c].[Name] AS [CategoryName],
			[c].[SortOrder] AS [CategorySortOrder],
			CASE
				WHEN @Term IS NULL THEN 0
				WHEN [p].[Sku] = @Term THEN 100
				WHEN [p].[ManufacturerPartNumber] = @Term THEN 95
				WHEN [p].[Sku] LIKE @Prefix ESCAPE N'\' THEN 80
				WHEN [p].[ProductName] LIKE @Prefix ESCAPE N'\' THEN 70
				WHEN [p].[ProductName] LIKE @Contains ESCAPE N'\' THEN 50
				WHEN [p].[ManufacturerPartNumber] LIKE @Contains ESCAPE N'\' THEN 40
				WHEN [p].[Description] LIKE @Contains ESCAPE N'\' THEN 20
				ELSE 0
			END AS [Relevance]
		FROM [dbo].[Product] p
		INNER JOIN [dbo].[CategoryMapping] m
			ON m.[SiteId] = @SiteId
			AND m.[FeedValue] = p.[Category]
		INNER JOIN [dbo].[SiteCategory] c
			ON c.[Id] = m.[SiteCategoryId]
			-- CategoryMapping.SiteCategoryId is an FK to *a* category, not to one this site
			-- owns — nothing ties a mapping's SiteId to the SiteId of the category it points
			-- at. Scoping only the mapping leaves one mistyped id enough to put another
			-- store's category name and slug into this store's catalog and navigation.
			AND c.[SiteId] = @SiteId
			AND c.[IsActive] = 1
		WHERE p.[Published] = 1
		  AND p.[Delisted] = 0
		  AND (@CategorySlug IS NULL OR c.[Slug] = @CategorySlug)
		  AND (@Brand IS NULL OR p.[Manufacturer] = @Brand)
		  AND (@InStockOnly = 0 OR p.[QuantityInStock] > 0)
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
		            AND g.[Value] = p.[Distributor])
	)
	SELECT
		[Id], [Sku], [ProductName], [Description], [RetailPrice], [Cost], [QuantityInStock],
		[ProductImage], [Manufacturer], [ManufacturerPartNumber], [Distributor], [Source],
		[Badge], [Featured], [CategorySlug], [CategoryName],
		-- One window function rather than a second query: the caller needs the total to build
		-- the pager, and a separate COUNT would re-evaluate the whole predicate.
		COUNT(*) OVER () AS [TotalCount]
	FROM [visible]
	WHERE @Term IS NULL OR [Relevance] > 0
	ORDER BY
		-- A search always ranks by relevance first, whatever the sort control says; sorting a
		-- keyword result by name buries the exact SKU match.
		CASE WHEN @Term IS NULL THEN 0 ELSE [Relevance] END DESC,
		CASE WHEN @Sort = 'price-asc' THEN [RetailPrice] END ASC,
		CASE WHEN @Sort = 'price-desc' THEN [RetailPrice] END DESC,
		CASE WHEN @Sort = 'name' THEN [ProductName] END ASC,
		CASE WHEN @Sort IS NULL OR @Sort = 'featured' THEN [Featured] END DESC,
		[CategorySortOrder],
		-- Ties broken on the key so paging is stable; without it a product can appear on two
		-- consecutive pages and another on neither.
		[Id]
	OFFSET (@Page - 1) * @PageSize ROWS
	FETCH NEXT @PageSize ROWS ONLY;
END
