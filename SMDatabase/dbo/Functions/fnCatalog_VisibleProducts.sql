/*
Everything this store's catalog may show a given customer group, with a relevance score for an
optional search term and the net price that score would be sorted by.

Exists because spCatalog_Search and spCatalog_GetFacets have to agree on visibility exactly. A
facet count that disagrees with the page it filters to is a bug with no symptom until someone
counts, and until now the two carried separate copies of the same predicate kept in step by
discipline. One of them was already wrong once.

Inline on purpose — RETURNS TABLE with a single SELECT and no procedural body — so SQL Server
expands it into the caller's plan before optimisation. A multi-statement RETURNS @T TABLE would
be an opaque box with a fixed row estimate, which is exactly the wrong thing to put underneath
a paged query.

The consequence of being inline is that nothing here can DECLARE. @Term, @Prefix and @Contains
arrive pre-escaped, and @HasIncludeRule, @DiscountPct and @MinMarginPct pre-resolved, because
building them needs statements the caller has and this does not.
*/
CREATE FUNCTION [dbo].[fnCatalog_VisibleProducts]
(
	@SiteId int,
	@CustomerGroupId int,
	@HasIncludeRule bit,
	@CategorySlug nvarchar(80),
	@Brand nvarchar(100),
	@InStockOnly bit,
	@Term nvarchar(200),
	@Prefix nvarchar(402),
	@Contains nvarchar(404),
	-- Resolved by the caller from the group and the site, never accepted from a client. A
	-- caller that could pass a discount is a caller that could ask for 90% off.
	@DiscountPct int,
	@MinMarginPct decimal(5, 2),
	-- Likewise resolved by the caller, through dbo.fnSite_StaleBeforeUtc, because nothing here
	-- can DECLARE. NULL means the store does not hide stale products.
	@StaleBeforeUtc datetime2
)
RETURNS TABLE
AS
RETURN
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
		-- Detail-page only. Carried here rather than in a separate query so the detail page
		-- and the listing cannot drift apart on what is visible; an inline function's
		-- unreferenced output columns are eliminated from a caller that does not select them.
		[p].[Ean],
		[p].[LastSynced],
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
		END AS [Relevance],

		/*
		The price a price sort must order by.

		This is the one place in the codebase where SMDataManager.Library.Pricing.PriceResolver
		is duplicated, and the duplication is deliberate. Ordering by RetailPrice while
		displaying the resolved net is only correct while the margin floor cannot bind; the
		moment a site sets MinMarginPct above zero, the floored rows jump position and "price
		ascending" renders visibly out of order — worst on the thin-margin products a buyer
		studies hardest. Paging in memory instead would fix it and would not survive the SKU
		counts this platform is sized for.

		So: this expression orders, PriceResolver renders, and a differential test drives a
		matrix of inputs through both and asserts they agree to the cent. If you change one,
		that test is what tells you about the other.

		Note the shape, which is PriceResolver's and not the obvious one: the floor is rounded
		before it is capped, and the cap is against the *unrounded* list price.
		*/
		[resolved].[NetPrice]

	FROM [dbo].[Product] p

	INNER JOIN [dbo].[CategoryMapping] m
		ON m.[SiteId] = @SiteId
		AND m.[FeedValue] = p.[Category]

	INNER JOIN [dbo].[SiteCategory] c
		ON c.[Id] = m.[SiteCategoryId]
		-- CategoryMapping.SiteCategoryId is an FK to *a* category, not to one this site owns.
		-- FK_CategoryMapping_ToSiteCategory is composite so the mismatch cannot be stored, but
		-- the predicate keeps this query correct on its own terms.
		AND c.[SiteId] = @SiteId
		AND c.[IsActive] = 1

	-- Named intermediates rather than one nested CASE repeated three times. CROSS APPLY
	-- (VALUES ...) is the T-SQL idiom for it and costs nothing: the optimiser folds these into
	-- the containing expression.
	CROSS APPLY (VALUES (
		ROUND(CAST([p].[RetailPrice] AS decimal(19, 4))
		      * (1 - CAST(@DiscountPct AS decimal(9, 4)) / 100), 2)
	)) AS [discounted]([Net])

	CROSS APPLY (VALUES (
		-- NULL when no floor applies, which keeps the comparison below a single test. A feed
		-- row with no cost gets the group's rate as configured rather than a floor invented
		-- from nothing.
		CASE
			WHEN [p].[Cost] IS NOT NULL AND [p].[Cost] > 0 AND @MinMarginPct > 0
			THEN CASE
				-- Capped against the raw list price, not the rounded one, and a floor above
				-- list means the product is mispriced: raising the customer above list would
				-- be worse than honouring it, so the operator finds this in the margin report
				-- rather than through a complaint.
				WHEN ROUND(CAST([p].[Cost] AS decimal(19, 4))
				           * (1 + CAST(@MinMarginPct AS decimal(9, 4)) / 100), 2)
				     > CAST([p].[RetailPrice] AS decimal(19, 4))
				THEN CAST([p].[RetailPrice] AS decimal(19, 4))
				ELSE ROUND(CAST([p].[Cost] AS decimal(19, 4))
				           * (1 + CAST(@MinMarginPct AS decimal(9, 4)) / 100), 2)
			END
		END
	)) AS [floored]([Floor])

	-- Cast back to the scale money carries. Multiplying two decimals inflates the result's
	-- scale, and a sort key whose type depends on the arithmetic that produced it is a
	-- nuisance to compare against anything — including the C# decimal the differential test
	-- checks it against.
	CROSS APPLY (VALUES (
		CAST(
			CASE
				WHEN [floored].[Floor] IS NOT NULL AND [discounted].[Net] < [floored].[Floor]
				THEN [floored].[Floor]
				ELSE [discounted].[Net]
			END
		AS decimal(19, 4))
	)) AS [resolved]([NetPrice])

	WHERE p.[Published] = 1
	  AND p.[Delisted] = 0
	  /*
	  Stale distributor stock, hidden only when the store asked for it.

	  Delisted means the distributor said it no longer supplies the product. Stale means the
	  distributor has not said anything at all for longer than this store finds acceptable —
	  the file stopped arriving, the credentials expired, the SFTP host moved — and the quantity
	  and price on the row are whatever they were the last time it did. Selling from that is how
	  a customer orders stock nobody has.

	  Two conditions that are easy to get wrong, and both have to be here:

	  - Own stock is never stale. A product the store holds itself has no LastSynced, so a plain
	    "LastSynced >= @StaleBeforeUtc" would hide the entire own-brand catalog the first time a
	    store set a threshold. Source is nullable, which is why NULL is named explicitly rather
	    than left to an inequality that would evaluate to UNKNOWN and hide the row.
	  - A distributor row with no LastSynced at all is treated as stale. The upsert always
	    stamps it, so this cannot arise from a sync; if it arises some other way, nothing knows
	    when that stock was last confirmed and the conservative reading is not to sell it.
	  */
	  AND (@StaleBeforeUtc IS NULL
	       OR p.[Source] IS NULL
	       OR p.[Source] <> 'Distributor'
	       OR (p.[LastSynced] IS NOT NULL AND p.[LastSynced] >= @StaleBeforeUtc))
	  AND (@CategorySlug IS NULL OR c.[Slug] = @CategorySlug)
	  AND (@Brand IS NULL OR p.[Manufacturer] = @Brand)
	  AND (@InStockOnly = 0 OR p.[QuantityInStock] > 0)
	  -- An IncludeCategory rule turns the group's visibility into an allow-list; without one,
	  -- everything is visible except what the exclusions remove.
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
