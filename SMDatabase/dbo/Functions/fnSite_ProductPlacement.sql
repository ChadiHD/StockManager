/*
Where one product sits on one store, and why: the single answer to "does this store sell it".

Two callers, on purpose. dbo.fnCatalog_VisibleProducts asks it for every row the storefront
might show, and the admin's product list asks it for every row in the catalog. A screen that
worked out "on this store" for itself would be a second copy of the rule, and the day the
copies disagree the admin is told a product is on sale that no customer can find.

Per product rather than a table of every product, so a caller CROSS APPLYs it to the rows it is
already reading instead of joining dbo.Product to itself. Inline, so it expands into the
caller's plan like fnCatalog_VisibleProducts does.

First, whether the store may sell the product at all — its currency, and for distributor stock
its own feeds (dbo.fnSite_ProductSellable, T9). Then the store's override (dbo.SiteProduct)
wins over its category mapping, and the category must be one of this store's and active, or the
product has nowhere to be filed and is not on sale. Delisting, staleness and customer-group
rules are not placement -- they are the distributor's facts and the group's terms, and
fnCatalog_VisibleProducts applies them on top.

Reason is for the admin; the storefront reads OnStore.
  Elsewhere another store's stock, or priced in another currency: not this store's to place
  Hidden    the store hid it
  Shown     the store showed it, overriding or extending its mapping
  Mapped    its feed category maps to one of the store's categories
  Unmapped  nothing puts it on this store
*/
CREATE FUNCTION [dbo].[fnSite_ProductPlacement]
(
	@SiteId int,
	@ProductId int,
	-- dbo.Product.Category, FeedId, Source and CurrencyCode, passed by the caller that has
	-- already read them.
	@FeedCategory nvarchar(50),
	@FeedId int,
	@Source nvarchar(20),
	@CurrencyCode nvarchar(3)
)
RETURNS TABLE
AS
RETURN
(
	SELECT
		[c].[Id] AS [SiteCategoryId],
		[c].[Slug] AS [CategorySlug],
		[c].[Name] AS [CategoryName],
		[c].[SortOrder] AS [CategorySortOrder],
		CAST(CASE WHEN [sel].[Sellable] = 1 AND [c].[Id] IS NOT NULL AND ISNULL([sp].[Visibility], N'') <> N'Hide'
		          THEN 1 ELSE 0 END AS bit) AS [OnStore],
		CASE
			WHEN [sel].[Sellable] = 0 THEN N'Elsewhere'
			WHEN [sp].[Visibility] = N'Hide' THEN N'Hidden'
			WHEN [c].[Id] IS NULL THEN N'Unmapped'
			WHEN [sp].[Visibility] = N'Show' THEN N'Shown'
			ELSE N'Mapped'
		END AS [Reason],
		[sel].[Sellable],
		ISNULL([sp].[Featured], 0) AS [Featured],
		[sp].[Badge],
		[sp].[Visibility],
		-- The override's own category, distinct from where the product ends up, so the admin
		-- can tell "filed here by the mapping" from "filed here because we said so".
		[sp].[SiteCategoryId] AS [OverrideCategoryId]
	FROM [dbo].[fnSite_ProductSellable](@SiteId, @FeedId, @Source, @CurrencyCode) AS [sel]
	LEFT JOIN dbo.SiteProduct sp
		ON sp.[SiteId] = @SiteId
		AND sp.[ProductId] = @ProductId
	LEFT JOIN dbo.CategoryMapping m
		ON m.[SiteId] = @SiteId
		AND m.[FeedValue] = @FeedCategory
	LEFT JOIN dbo.SiteCategory c
		ON c.[Id] = CASE WHEN sp.[Visibility] = N'Show' AND sp.[SiteCategoryId] IS NOT NULL
		                 THEN sp.[SiteCategoryId]
		                 ELSE m.[SiteCategoryId] END
		-- Both FKs into SiteCategory are composite, so a foreign category cannot be stored;
		-- the predicate keeps this correct on its own terms, as fnCatalog_VisibleProducts does.
		AND c.[SiteId] = @SiteId
		AND c.[IsActive] = 1
)
