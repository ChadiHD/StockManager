/*
Facet counts for the catalog rail, under the same visibility rules as spCatalog_Search —
literally the same rules now, since both call dbo.fnCatalog_VisibleProducts. They used to hold
separate copies of one predicate, which is a class of bug with no symptom until someone counts
the rail against the page it filters to.

Counts are computed against the current filter minus the facet's own dimension, so the
category counts show what selecting each category would give rather than all collapsing to the
already-selected one. That is why @CategorySlug and @Brand are not passed to the function:
the unfiltered visible set goes into a temp table once and each grid applies the other
dimension itself. Availability is the exception — it is a narrowing toggle, so it is applied
to the base set and its count reflects the filter as it stands.

The temp table is not incidental. A CTE is in scope only for the statement immediately after
it and this procedure returns two grids, so a CTE gives "Invalid object name" on the second —
and calling the function twice would evaluate the whole predicate twice.
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

	-- The same degradation spCatalog_Search applies, and needed here for the same reason: a
	-- group from another store must not filter this store's counts through its allow-list.
	-- If the two procedures disagreed about which group is in play, the rail would count a
	-- different set from the one the page renders.
	IF @CustomerGroupId IS NOT NULL
	   AND NOT EXISTS (SELECT 1 FROM dbo.CustomerGroup
	                   WHERE [Id] = @CustomerGroupId AND [SiteId] = @SiteId)
	BEGIN
		SET @CustomerGroupId = NULL;
	END

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

	-- Zero discount and zero margin: counting rows does not need a price, and the function's
	-- NetPrice is an unreferenced output column here, which the optimiser drops. Passing the
	-- real values would mean duplicating the group and site lookups for a number nothing
	-- reads.
	INSERT INTO #visible ([Manufacturer], [CategorySlug], [CategoryName], [SortOrder])
	SELECT [Manufacturer], [CategorySlug], [CategoryName], [CategorySortOrder]
	FROM dbo.fnCatalog_VisibleProducts(
		@SiteId, @CustomerGroupId, @HasIncludeRule,
		-- Deliberately unfiltered on both facet dimensions; see the header.
		NULL, NULL,
		@InStockOnly, @Term, @Prefix, @Contains, 0, 0)
	WHERE @Term IS NULL OR [Relevance] > 0
	OPTION (RECOMPILE);

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
