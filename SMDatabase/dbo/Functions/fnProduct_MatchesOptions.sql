/*
Whether a style matches the options a shopper filtered by (T11): one row when it does.

A style matches when one of its live variants carries, for every option name selected, one of
the values selected for that name. Size M or L, and Navy, is an M or an L that is Navy, not any
M beside any Navy. With in-stock-only on, the matching variant has to be in stock as well: a
shopper asking for M in stock is not helped by a Navy L.

One definition, read by spCatalog_Search and spCatalog_GetFacets, for the reason
fnCatalog_VisibleProducts exists: the facet counts must agree with the page they filter to.
The callers test whether any option was selected first and skip this entirely when none was,
so a catalogue nobody is filtering by size pays nothing for it.
*/
CREATE FUNCTION [dbo].[fnProduct_MatchesOptions]
(
	@ProductId int,
	@InStockOnly bit,
	@Options dbo.ProductOptionList READONLY
)
RETURNS TABLE
AS
RETURN
(
	SELECT 1 AS [Matches]
	WHERE EXISTS (
		SELECT 1
		FROM [dbo].[Product] v
		WHERE [v].[ParentProductId] = @ProductId
		  AND [v].[Delisted] = 0
		  AND (@InStockOnly = 0 OR [v].[QuantityInStock] > 0)
		  -- No selected name this variant fails to carry one of the selected values for.
		  AND NOT EXISTS (
		      SELECT 1 FROM @Options wanted
		      WHERE NOT EXISTS (
		          SELECT 1
		          FROM [dbo].[ProductOption] o
		          INNER JOIN @Options chosen ON chosen.[Name] = [o].[Name] AND chosen.[Value] = [o].[Value]
		          WHERE [o].[ProductId] = [v].[Id] AND [o].[Name] = wanted.[Name])))
)
