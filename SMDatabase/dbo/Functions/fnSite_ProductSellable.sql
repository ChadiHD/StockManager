/*
Whether a store may sell a product at all (T9), before anything about where it sits: one row,
[Sellable] 1 or 0.

  - The product must be priced in the store's currency. RetailPrice and Cost are bare numbers in
    Product.CurrencyCode, and a store formats every price in its own.
  - A distributor product belongs to the feed that imported it, and so to that feed's store. A
    store has no account with another store's distributor, and the price is that distributor's.
  - Own stock — FeedId NULL and not marked Distributor — may be sold by any store in its
    currency, as one physical product could always be sold by two stores.
  - A distributor product with no feed is an orphan: its feed was deleted, or it predates FeedId
    and its feed's name was ambiguous. No store sells it.

The rule keys on FeedId rather than Source, because spProduct_Update lets an admin rewrite
Source, and an imported product relabelled "Own" would otherwise go on sale in every store in its
currency.

Takes the product's own columns, like dbo.fnSite_ProductPlacement, and is applied per row with
CROSS APPLY, so the optimiser folds it into the caller's scan of dbo.Product rather than
scanning the table a second time. dbo.fnSite_ProductPlacement applies it, so OnStore already
means "sellable and placed"; the admin's products and categories screens, the placement writes
and the quote submit read it directly. One definition, so none of them can disagree.
*/
CREATE FUNCTION [dbo].[fnSite_ProductSellable]
(
	@SiteId int,
	-- dbo.Product.FeedId, Source and CurrencyCode, passed by a caller that has read them.
	@FeedId int,
	@Source nvarchar(20),
	@CurrencyCode nvarchar(3)
)
RETURNS TABLE
AS
RETURN
(
	/*
	Joins, not EXISTS inside a CASE. Written that way first, the subqueries could not be turned
	into joins and ran once per product, and the 50,000-product load check's store-wide browse
	took three times as long. This is fnSite_ProductPlacement's own shape, which the optimiser
	decorrelates into the caller's scan.
	*/
	SELECT CAST(CASE
		WHEN [s].[Id] IS NOT NULL
		     AND ([f].[Id] IS NOT NULL
		          OR (@FeedId IS NULL AND ISNULL(@Source, N'Own') <> N'Distributor'))
		THEN 1 ELSE 0 END AS bit) AS [Sellable]
	FROM (VALUES (1)) AS [one]([x])
	LEFT JOIN dbo.Site s
		ON s.[Id] = @SiteId
		AND s.[CurrencyCode] = @CurrencyCode
	LEFT JOIN dbo.DistributorFeed f
		ON f.[Id] = @FeedId
		AND f.[SiteId] = @SiteId
)
