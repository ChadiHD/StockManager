/*
What tells each variant of a style from another, as a customer reads it (T11): "M / Navy".

A variant's own name is its style's followed by this, so a basket, a quote, an order and the
mail about it all say which size was bought with no change to any of them. One definition, read
by spProduct_AddVariant, by spProduct_Update when the style is renamed, and by the admin's
variants panel, so the three cannot come to name one variant two ways.

Every variant's label at once, for a caller to join and filter. Inline, so a filter on
ProductId is pushed into the aggregate.
*/
CREATE FUNCTION [dbo].[fnProduct_VariantLabels]()
RETURNS TABLE
AS
RETURN
(
	SELECT [o].[ProductId],
	       STRING_AGG(CAST([o].[Value] AS nvarchar(max)), N' / ')
	           WITHIN GROUP (ORDER BY [o].[SortOrder], [o].[Name]) AS [Label]
	FROM [dbo].[ProductOption] o
	GROUP BY [o].[ProductId]
)
