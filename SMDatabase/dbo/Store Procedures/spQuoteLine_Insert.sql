CREATE PROCEDURE [dbo].[spQuoteLine_Insert]
	@Id int output,
	@QuoteId int,
	@ProductId int,
	@Quantity int,
	@ListPrice money,
	@DiscountPct int
AS
BEGIN
	SET NOCOUNT ON;

	-- Net price is derived so the discount and the stored price can never disagree.
	DECLARE @NetPrice money = @ListPrice - (@ListPrice * @DiscountPct / 100.0);

	INSERT INTO dbo.QuoteLine([QuoteId], [ProductId], [Quantity], [ListPrice], [DiscountPct], [NetPrice])
	VALUES (@QuoteId, @ProductId, @Quantity, @ListPrice, @DiscountPct, @NetPrice);

	SELECT @Id = SCOPE_IDENTITY();
END
