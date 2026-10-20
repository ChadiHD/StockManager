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
it and this procedure returns three grids, so a CTE gives "Invalid object name" on the second —
and calling the function twice would evaluate the whole predicate twice.
*/
CREATE PROCEDURE [dbo].[spCatalog_GetFacets]
	@SiteId int,
	@CustomerGroupId int = NULL,
	@CategorySlug nvarchar(80) = NULL,
	@Brand nvarchar(100) = NULL,
	@InStockOnly bit = 0,
	@Search nvarchar(200) = NULL,
	-- spCatalog_Search's, with the same meaning (T11).
	@Options dbo.ProductOptionList READONLY
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @HasOptions bit = CASE WHEN EXISTS (SELECT 1 FROM @Options) THEN 1 ELSE 0 END;

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

	-- The same cutoff spCatalog_Search resolves, and it has to be the same: a facet count that
	-- includes stale products while the page they filter to excludes them is a bug with no
	-- symptom until somebody counts. Resolved into a variable, never called per row.
	DECLARE @StaleBeforeUtc datetime2 = dbo.fnSite_StaleBeforeUtc(@SiteId);

	CREATE TABLE #visible
	(
		[Id] INT NOT NULL,
		[Manufacturer] NVARCHAR(100) NULL,
		[CategorySlug] NVARCHAR(80) NOT NULL,
		[CategoryName] NVARCHAR(120) NOT NULL,
		[SortOrder] INT NOT NULL
	);

	-- Zero discount and zero margin: counting rows does not need a price, and the function's
	-- NetPrice is an unreferenced output column here, which the optimiser drops. Passing the
	-- real values would mean duplicating the group and site lookups for a number nothing
	-- reads.
	INSERT INTO #visible ([Id], [Manufacturer], [CategorySlug], [CategoryName], [SortOrder])
	SELECT [Id], [Manufacturer], [CategorySlug], [CategoryName], [CategorySortOrder]
	FROM dbo.fnCatalog_VisibleProducts(
		@SiteId, @CustomerGroupId, @HasIncludeRule,
		-- Deliberately unfiltered on both facet dimensions; see the header.
		NULL, NULL,
		@InStockOnly, @Term, @Prefix, @Contains, 0, 0, @StaleBeforeUtc)
	-- Styles only (T11), as the listing counts them.
	WHERE (@Term IS NULL OR [Relevance] > 0) AND [ParentProductId] IS NULL
	  -- The page's own option filter, so the category and brand counts agree with it.
	  AND (@HasOptions = 0 OR EXISTS (SELECT 1 FROM dbo.fnProduct_MatchesOptions([Id], @InStockOnly, @Options)))
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

	/*
	Options (T11): how many styles have a live variant with each value, within the category and
	brand chosen. Counted over the set the option filter already narrowed, unlike the two grids
	above, which each leave their own dimension out; excluding each option name from its own
	count would mean one pass per name, for a rail that reads well enough without it.

	An option's name is the store's word, so the rail groups on whatever the store wrote. Values
	come in the order their first variant was added, which is the order the admin entered sizes
	(S, M, L) rather than the alphabet's (L, M, S).
	*/
	SELECT [o].[Name], [o].[Value], COUNT(DISTINCT [v].[ParentProductId]) AS [Count]
	FROM #visible s
	INNER JOIN dbo.Product v
		ON [v].[ParentProductId] = s.[Id]
		AND [v].[Delisted] = 0
		AND (@InStockOnly = 0 OR [v].[QuantityInStock] > 0)
	INNER JOIN dbo.ProductOption o ON [o].[ProductId] = [v].[Id]
	WHERE (@CategorySlug IS NULL OR s.[CategorySlug] = @CategorySlug)
	  AND (@Brand IS NULL OR s.[Manufacturer] = @Brand)
	GROUP BY [o].[Name], [o].[Value]
	ORDER BY MIN([o].[SortOrder]), [o].[Name], MIN([v].[Id]);

	DROP TABLE #visible;
END
