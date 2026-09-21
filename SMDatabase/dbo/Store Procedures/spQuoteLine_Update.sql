/*
Re-prices or re-quantifies one line of a quote.

QuoteId and SiteId are part of the predicate rather than something the caller checks first,
exactly as in spQuoteLine_Delete: neither a guessed line id nor a guessed quote id can re-price
a line on another quote, or on another store's quote.

It refuses a line on an Accepted quote. spOrder_ConvertFromQuote has already copied these lines
onto a Purchase by then, so a re-price here would leave the quote and the order disagreeing
about what was sold -- in money, on two documents, with nothing recording which one moved. The
portal withdraws the control for the same reason, but a page is not a guard: this is an API an
admin token can post to directly.

Returns the row count, so a line that belonged elsewhere, or a quote already accepted, reports
0 rather than reading as a successful write.
*/
CREATE PROCEDURE [dbo].[spQuoteLine_Update]
	@Id int,
	@QuoteId int,
	@SiteId int,
	@Quantity int,
	@DiscountPct decimal(5, 2),
	-- What to charge per unit. NULL means "work it out from the line's list price and this
	-- discount", which is what an admin typing a discount into the portal wants. Stated
	-- explicitly, it is stored as given -- the rule spQuoteLine_Insert documents at length.
	@NetPrice money = NULL
AS
BEGIN
	SET NOCOUNT ON;

	IF @Quantity IS NULL OR @Quantity < 1
	BEGIN
		THROW 50033, 'A quote line needs a quantity of at least one.', 1;
	END

	-- Read from the row rather than taken as a parameter: the list price is the catalogue's to
	-- state, and a caller that could send one could quote a price the store never set.
	DECLARE @ListPrice money;

	SELECT @ListPrice = [l].[ListPrice]
	FROM dbo.QuoteLine l
	INNER JOIN dbo.Quote q
		ON q.[Id] = l.[QuoteId]
		AND q.[SiteId] = @SiteId
	WHERE [l].[Id] = @Id
	  AND [l].[QuoteId] = @QuoteId;

	IF @ListPrice IS NULL
	BEGIN
		SELECT 0 AS [Updated];
		RETURN;
	END

	IF @NetPrice IS NULL
	BEGIN
		-- PriceResolver's shape, and fnCatalog_VisibleProducts': cast out of money before
		-- multiplying, then round to two places. T-SQL's ROUND is half-away-from-zero, which
		-- is MidpointRounding.AwayFromZero and not the framework default -- an edited line and
		-- an inserted one must not round differently.
		SET @NetPrice = ROUND(CAST(@ListPrice AS decimal(19, 4))
		                      * (1 - CAST(@DiscountPct AS decimal(9, 4)) / 100), 2);
	END

	UPDATE [l]
	SET [l].[Quantity] = @Quantity,
	    [l].[DiscountPct] = @DiscountPct,
	    [l].[NetPrice] = @NetPrice
	FROM dbo.QuoteLine l
	INNER JOIN dbo.Quote q
		ON q.[Id] = l.[QuoteId]
		AND q.[SiteId] = @SiteId
	WHERE [l].[Id] = @Id
	  AND [l].[QuoteId] = @QuoteId
	  AND [q].[Status] <> 'Accepted';

	SELECT @@ROWCOUNT AS [Updated];
END
