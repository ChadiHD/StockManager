/*
A basket's lines, with enough of each product for the page to price and render it.

No price is computed here. PriceResolver computes the price a customer is shown, and this
returns the inputs it takes — RetailPrice and Cost — exactly as spCatalog_Search does. A net
price returned from here would be a fourth copy of a rule that already exists twice by
deliberate exception.

Availability is the one thing this decides, and it decides it through
dbo.fnCatalog_VisibleProducts so that "available" means what it means everywhere else. A line
can survive its product: the product was delisted, went stale, left the store's category
mapping, or the customer signed in to a group whose rules exclude it. Those lines are returned
with Available = 0 rather than dropped, because a basket that silently loses rows is a basket
the customer cannot reason about.
*/
CREATE PROCEDURE [dbo].[spBasket_GetLines]
	@BasketId int,
	@SiteId int,
	@CustomerGroupId int = NULL
AS
BEGIN
	SET NOCOUNT ON;

	-- The same degradation the catalog applies: a group from another store is treated as no
	-- group rather than as an error.
	IF @CustomerGroupId IS NOT NULL
	   AND NOT EXISTS (SELECT 1 FROM dbo.CustomerGroup
	                   WHERE [Id] = @CustomerGroupId AND [SiteId] = @SiteId)
	BEGIN
		SET @CustomerGroupId = NULL;
	END

	DECLARE @HasIncludeRule bit =
		CASE WHEN @CustomerGroupId IS NULL THEN 0
		     WHEN EXISTS (SELECT 1 FROM dbo.GroupVisibility
		                  WHERE [CustomerGroupId] = @CustomerGroupId AND [Rule] = 'IncludeCategory')
		     THEN 1 ELSE 0 END;

	DECLARE @StaleBeforeUtc datetime2 = dbo.fnSite_StaleBeforeUtc(@SiteId);

	SELECT
		[l].[Id],
		[l].[BasketId],
		[l].[ProductId],
		[p].[Sku],
		[p].[ProductName] AS [Name],
		[l].[Quantity],
		[p].[RetailPrice],
		[p].[Cost],
		[p].[QuantityInStock],
		[l].[AddedUtc],
		CASE WHEN [v].[Id] IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END AS [Available]
	FROM dbo.BasketLine l
	-- The basket is joined rather than assumed, so a guessed basket id returns nothing instead
	-- of another viewer's lines.
	INNER JOIN dbo.Basket b
		ON b.[Id] = l.[BasketId]
		AND b.[SiteId] = @SiteId
	INNER JOIN dbo.Product p ON p.[Id] = l.[ProductId]
	LEFT JOIN dbo.fnCatalog_VisibleProducts(
		@SiteId, @CustomerGroupId, @HasIncludeRule, NULL, NULL, 0, NULL, NULL, NULL, 0, 0,
		@StaleBeforeUtc) v ON v.[Id] = l.[ProductId]
	WHERE [l].[BasketId] = @BasketId
	ORDER BY [l].[Id];
END
