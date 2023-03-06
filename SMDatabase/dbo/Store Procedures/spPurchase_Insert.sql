CREATE PROCEDURE [dbo].[spPurchase_Insert]
	@Id int output,
	@StaffId nvarchar(128),
	@PurchaseDate datetime2,
	@SubTotal money,
	@VAT money,
	@FinalPrice money

AS
BEGIN
	SET NOCOUNT ON;


	INSERT INTO dbo.Purchase(StaffId, PurchaseDate, SubTotal, VAT, FinalPrice)
	VALUES (@StaffId, @PurchaseDate, @SubTotal, @VAT, @FinalPrice)

	SELECT @Id = SCOPE_IDENTITY();

END
