CREATE PROCEDURE [dbo].[spQuoteLine_Insert]
	@Id int output,
	@QuoteId int,
	@ProductId int,
	@Quantity int,
	@ListPrice money,
	@DiscountPct decimal(5, 2),
	@SiteId int,
	-- The price the customer was shown. NULL means "work it out", which is what an admin
	-- typing a list price and a discount into the portal wants.
	@NetPrice money = NULL
AS
BEGIN
	SET NOCOUNT ON;

	-- Adding a line is a write against the quote, so it is gated on the quote belonging to
	-- this store rather than on the caller having checked.
	IF NOT EXISTS (SELECT 1 FROM dbo.Quote WHERE [Id] = @QuoteId AND [SiteId] = @SiteId)
	BEGIN
		THROW 50004, 'That quote does not belong to this store.', 1;
	END

	/*
	The caller states the net price, and this derives one only when it did not.

	Until T5 the derivation was unconditional, and it was right for the only caller there was:
	an admin typing a list price and a whole-number discount. It is wrong for the storefront,
	because the price a customer was shown comes out of PriceResolver and cannot always be
	reconstructed from (ListPrice, DiscountPct):

	  - A price held up by Site.MinMarginPct is cost x (1 + margin). The discount that produces
	    it is whatever it happens to be, and reconstructing net from a rounded discount gives a
	    different number.
	  - Even with no floor, the old expression did not round. money carries four decimal
	    places, so 99.99 at 7% stored 92.9907 while the page displayed 92.99 — a price nobody
	    can pay, on the document the customer signs.

	So: the price a customer was shown is the price that gets stored. That is the same rule
	CatalogPriceParityTests already enforces for the catalog — SQL computes an ordering key,
	PriceResolver computes the price a customer is shown — carried one step further, to what
	gets written down.

	The derivation keeps PriceResolver's exact shape, which is also
	fnCatalog_VisibleProducts': cast out of money before multiplying, then round to two places.
	T-SQL's ROUND is half-away-from-zero, which is PriceResolver's MidpointRounding.AwayFromZero
	and not the framework default — two lines of the same product must not round differently.
	*/
	IF @NetPrice IS NULL
	BEGIN
		SET @NetPrice = ROUND(CAST(@ListPrice AS decimal(19, 4))
		                      * (1 - CAST(@DiscountPct AS decimal(9, 4)) / 100), 2);
	END

	INSERT INTO dbo.QuoteLine([QuoteId], [ProductId], [Quantity], [ListPrice], [DiscountPct], [NetPrice])
	VALUES (@QuoteId, @ProductId, @Quantity, @ListPrice, @DiscountPct, @NetPrice);

	SELECT @Id = SCOPE_IDENTITY();
END
