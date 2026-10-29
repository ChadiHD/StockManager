/*
The tax on one order line, for both writers of orders: spOrder_ConvertFromQuote and
spOrder_PlaceFromBasket (T12). Written twice, the rule drifts.

The rate reaches a line only if its product is taxable.

ROUND, half away from zero, matching TaxAssessment.On and PriceResolver and the expression in
fnCatalog_VisibleProducts. A line taxed by one rounding rule and priced by another is two
numbers on one document that do not add up.

On a store whose prices include tax (T10) the tax is taken out of the line rather than put on
top of it: qty x price x rate / (100 + rate). The line keeps the price the customer was shown,
so the order's total is exactly the sum of those prices. Taxing the net figure and adding it
back drifts from that sum by a cent within ten units.

Inline, and applied with CROSS APPLY, so it folds into the INSERT ... SELECT that copies the
lines rather than running per row.
*/
CREATE FUNCTION [dbo].[fnOrder_LineTax]
(
	@Quantity int,
	@Price money,
	@TaxRatePct decimal(5, 2),
	@IsTaxable bit,
	@Inclusive bit
)
RETURNS TABLE
AS
RETURN
(
	SELECT CAST(CASE
		WHEN @IsTaxable = 0 THEN 0
		WHEN @Inclusive = 1
		THEN ROUND(CAST(@Quantity * @Price AS decimal(19, 4))
		           * CAST(@TaxRatePct AS decimal(9, 4)) / (100 + CAST(@TaxRatePct AS decimal(9, 4))), 2)
		ELSE ROUND(CAST(@Quantity * @Price AS decimal(19, 4))
		           * CAST(@TaxRatePct AS decimal(9, 4)) / 100, 2) END AS money) AS [Vat],
	       CAST(CASE WHEN @IsTaxable = 1 THEN @TaxRatePct ELSE 0 END AS decimal(5, 2)) AS [RatePct]
)
