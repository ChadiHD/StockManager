/*
Paged, faceted, site-scoped product search for the storefront.

Four things are deliberate:

1. Visibility is decided inside the query, not filtered afterwards. A product removed from an
   already-fetched page has still been counted, still shifted the paging, and has usually
   already reached the browser. Every gate lives in dbo.fnCatalog_VisibleProducts, which this
   procedure and spCatalog_GetFacets both call rather than each keeping a copy.

2. A product belongs to this store when its feed category has a dbo.CategoryMapping row for
   the site — dbo.Product carries no SiteId, because the desktop POS and own stock have no
   site and one product may be sold by two stores. An unmapped feed value is invisible rather
   than uncategorised.

3. Matching is literal, never fuzzy. Sibling identifiers (SKU, MPN, EAN) are the same shape as
   one another, and approximate matching on them returns confidently wrong products — the same
   reason FuzzySearch gives code fields no fuzzy pass.

4. The discount and the margin floor are resolved here, from the group and the site, and are
   not parameters. A caller that could pass a discount is a caller that could ask for 90% off.

Full-text was the intended mechanism and is not used: the development SQL Server container
reports IsFullTextInstalled = 0, so CONTAINSTABLE is unavailable there even though Azure SQL
supports it. The ranking is portable and correct at this catalog's size; swapping it for
CONTAINSTABLE later is a change to fnCatalog_VisibleProducts alone.

Why browsing branches per sort and searching does not
-----------------------------------------------------
A single ORDER BY picking between four columns with CASE can never match an index's key order:
the optimiser cannot know which column governs the sort until the CASE is evaluated per row,
so it can never walk an index and stop once @PageSize rows are out. Page 1 of an unfiltered
browse then pays to rank the entire catalog. Branching gives each browse sort a plain column
reference the optimiser can actually use.

A search does not get the same treatment, and does not need it. Its result is already reduced
to the rows that matched, it must lead on relevance whatever the sort control says — sorting
keyword results by name buries the exact SKU match — and the sort is still wanted as a
tiebreak within equal relevance. One CASE ladder over a filtered set is cheap; four more
copies of a seventeen-column projection to avoid it would be a worse trade than the one being
made.

OPTION (RECOMPILE) is on every branch because fnCatalog_VisibleProducts is a wall of optional
"@X IS NULL OR col = @X" filters. Whichever combination first compiles a plan gets reused for
every other, and "one category" versus "the whole catalog" versus "a search term" differ in
selectivity by orders of magnitude. RECOMPILE also re-sniffs local variables — @Term, @Prefix,
@Contains, @HasIncludeRule and the resolved discount are all DECLAREd, and a cached plan would
estimate them off a generic density guess rather than their actual values. At this catalog's
size the compile is a poor trade against a wrong plan's reads, and it gets better as the
catalog grows.
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
	-- branch and silently landing in an arbitrary order.
	SET @Sort = CASE WHEN @Sort IN (N'price-asc', N'price-desc', N'name', N'featured')
	                 THEN @Sort ELSE N'featured' END;

	/*
	A group belonging to another store is treated as no group at all, rather than as an error.

	Both halves matter. Its Discount must not price this store's catalog, and its
	GroupVisibility rules must not filter it — a tampered session that kept the id would
	otherwise see this store's products through another store's allow-list. Degrading to list
	price and no rules is the safe reading, and it gives an attacker nothing to probe with:
	a wrong id and an id from elsewhere are indistinguishable in the response.
	*/
	IF @CustomerGroupId IS NOT NULL
	   AND NOT EXISTS (SELECT 1 FROM dbo.CustomerGroup
	                   WHERE [Id] = @CustomerGroupId AND [SiteId] = @SiteId)
	BEGIN
		SET @CustomerGroupId = NULL;
	END

	DECLARE @DiscountPct int = 0;

	SELECT @DiscountPct = [Discount]
	FROM dbo.CustomerGroup
	WHERE [Id] = @CustomerGroupId;

	-- Clamped to match PriceResolver, which does the same before applying it. A group row
	-- edited to 150 or -20 should not invert a price.
	SET @DiscountPct = CASE WHEN ISNULL(@DiscountPct, 0) < 0 THEN 0
	                        WHEN @DiscountPct > 100 THEN 100
	                        ELSE @DiscountPct END;

	DECLARE @MinMarginPct decimal(5, 2) =
		ISNULL((SELECT [MinMarginPct] FROM dbo.Site WHERE [Id] = @SiteId), 0);

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

	/*
	Resolved once into a variable, never per row.

	fnSite_StaleBeforeUtc is a scalar function, and a scalar function in a WHERE or a SELECT
	list runs for every row and defeats the plan. It is here for the same reason @DiscountPct
	and @MinMarginPct are: fnCatalog_VisibleProducts is inline and cannot DECLARE, so the
	cutoff has to arrive already computed.

	spCatalog_GetFacets resolves the identical value. That is not optional — the facet counts
	and the page they filter to must agree exactly, which is the entire reason the visibility
	predicate lives in one function.
	*/
	DECLARE @StaleBeforeUtc datetime2 = dbo.fnSite_StaleBeforeUtc(@SiteId);

	DECLARE @Offset int = (@Page - 1) * @PageSize;

	/*
	NetPrice is not in any projection below, only in the ORDER BY of the two price branches.

	It is an ordering key, not a price. The price a customer sees comes from PriceResolver in
	SMDataManager.Library, which is the one implementation allowed to answer that question;
	leaving the column out of the SELECT is what makes rendering the other one impossible
	rather than merely discouraged.
	*/

	IF @Term IS NOT NULL
	BEGIN
		SELECT
			[Id], [Sku], [ProductName], [Description], [RetailPrice], [Cost], [QuantityInStock],
			[ProductImage], [Manufacturer], [ManufacturerPartNumber], [Distributor], [Source],
			[Badge], [Featured], [CategorySlug], [CategoryName],
			-- One window function rather than a second query: the caller needs the total to
			-- build the pager, and a separate COUNT would re-evaluate the whole predicate.
			COUNT(*) OVER () AS [TotalCount]
		FROM dbo.fnCatalog_VisibleProducts(
			@SiteId, @CustomerGroupId, @HasIncludeRule, @CategorySlug, @Brand, @InStockOnly,
			@Term, @Prefix, @Contains, @DiscountPct, @MinMarginPct, @StaleBeforeUtc)
		WHERE [Relevance] > 0
		ORDER BY
			[Relevance] DESC,
			CASE WHEN @Sort = N'price-asc'  THEN [NetPrice]    END ASC,
			CASE WHEN @Sort = N'price-desc' THEN [NetPrice]    END DESC,
			CASE WHEN @Sort = N'name'       THEN [ProductName] END ASC,
			CASE WHEN @Sort = N'featured'   THEN [Featured]    END DESC,
			[CategorySortOrder],
			-- Ties broken on the key so paging is stable; without it a product can appear on
			-- two consecutive pages and another on neither.
			[Id]
		OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
		OPTION (RECOMPILE);
	END
	ELSE IF @Sort = N'price-asc'
	BEGIN
		SELECT
			[Id], [Sku], [ProductName], [Description], [RetailPrice], [Cost], [QuantityInStock],
			[ProductImage], [Manufacturer], [ManufacturerPartNumber], [Distributor], [Source],
			[Badge], [Featured], [CategorySlug], [CategoryName],
			COUNT(*) OVER () AS [TotalCount]
		FROM dbo.fnCatalog_VisibleProducts(
			@SiteId, @CustomerGroupId, @HasIncludeRule, @CategorySlug, @Brand, @InStockOnly,
			@Term, @Prefix, @Contains, @DiscountPct, @MinMarginPct, @StaleBeforeUtc)
		ORDER BY [NetPrice] ASC, [Id]
		OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
		OPTION (RECOMPILE);
	END
	ELSE IF @Sort = N'price-desc'
	BEGIN
		SELECT
			[Id], [Sku], [ProductName], [Description], [RetailPrice], [Cost], [QuantityInStock],
			[ProductImage], [Manufacturer], [ManufacturerPartNumber], [Distributor], [Source],
			[Badge], [Featured], [CategorySlug], [CategoryName],
			COUNT(*) OVER () AS [TotalCount]
		FROM dbo.fnCatalog_VisibleProducts(
			@SiteId, @CustomerGroupId, @HasIncludeRule, @CategorySlug, @Brand, @InStockOnly,
			@Term, @Prefix, @Contains, @DiscountPct, @MinMarginPct, @StaleBeforeUtc)
		ORDER BY [NetPrice] DESC, [Id]
		OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
		OPTION (RECOMPILE);
	END
	ELSE IF @Sort = N'name'
	BEGIN
		SELECT
			[Id], [Sku], [ProductName], [Description], [RetailPrice], [Cost], [QuantityInStock],
			[ProductImage], [Manufacturer], [ManufacturerPartNumber], [Distributor], [Source],
			[Badge], [Featured], [CategorySlug], [CategoryName],
			COUNT(*) OVER () AS [TotalCount]
		FROM dbo.fnCatalog_VisibleProducts(
			@SiteId, @CustomerGroupId, @HasIncludeRule, @CategorySlug, @Brand, @InStockOnly,
			@Term, @Prefix, @Contains, @DiscountPct, @MinMarginPct, @StaleBeforeUtc)
		ORDER BY [ProductName] ASC, [Id]
		OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
		OPTION (RECOMPILE);
	END
	ELSE
	BEGIN
		SELECT
			[Id], [Sku], [ProductName], [Description], [RetailPrice], [Cost], [QuantityInStock],
			[ProductImage], [Manufacturer], [ManufacturerPartNumber], [Distributor], [Source],
			[Badge], [Featured], [CategorySlug], [CategoryName],
			COUNT(*) OVER () AS [TotalCount]
		FROM dbo.fnCatalog_VisibleProducts(
			@SiteId, @CustomerGroupId, @HasIncludeRule, @CategorySlug, @Brand, @InStockOnly,
			@Term, @Prefix, @Contains, @DiscountPct, @MinMarginPct, @StaleBeforeUtc)
		ORDER BY [Featured] DESC, [CategorySortOrder], [Id]
		OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
		OPTION (RECOMPILE);
	END
END
