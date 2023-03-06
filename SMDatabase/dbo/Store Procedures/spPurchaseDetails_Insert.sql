CREATE PROCEDURE [dbo].[spPurchaseDetails_Insert]
	@PurchaseId int,
	@ProductId int,
	@Quantity int,
	@PurchasePrice money,
	@VAT money
AS
BEGIN
SET NOCOUNT ON;

	INSERT INTO dbo.PurchaseDetail(PurchaseId, ProductId, Quantity, PurchasePrice, VAT)
	VALUES (@PurchaseId, @ProductId, @Quantity, @PurchasePrice, @VAT);

END
