CREATE PROCEDURE [dbo].[spQuoteLine_Insert]
	@Id int output,
	@QuoteId int,
	@ProductId int,
	@Quantity int,
	@ListPrice money,
	@DiscountPct int,
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	-- Adding a line is a write against the quote, so it is gated on the quote belonging to
	-- this store rather than on the caller having checked.
	IF NOT EXISTS (SELECT 1 FROM dbo.Quote WHERE [Id] = @QuoteId AND [SiteId] = @SiteId)
	BEGIN
		THROW 50004, 'That quote does not belong to this store.', 1;
	END

	-- Net price is derived so the discount and the stored price can never disagree.
	DECLARE @NetPrice money = @ListPrice - (@ListPrice * @DiscountPct / 100.0);

	INSERT INTO dbo.QuoteLine([QuoteId], [ProductId], [Quantity], [ListPrice], [DiscountPct], [NetPrice])
	VALUES (@QuoteId, @ProductId, @Quantity, @ListPrice, @DiscountPct, @NetPrice);

	SELECT @Id = SCOPE_IDENTITY();
END
