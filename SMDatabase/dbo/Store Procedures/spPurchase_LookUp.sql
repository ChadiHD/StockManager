CREATE PROCEDURE [dbo].[spPurchase_LookUp]
	@StaffId nvarchar(128),
	@PurchaseDate datetime2
AS
BEGIN
SET NOCOUNT ON;

	SELECT Id
	FROM dbo.Purchase
	WHERE StaffId = @StaffId AND PurchaseDate = @PurchaseDate

END
