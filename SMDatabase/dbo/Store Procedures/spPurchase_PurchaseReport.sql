CREATE PROCEDURE [dbo].[spPurchase_PurchaseReport]
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [s].[PurchaseDate], [s].[SubTotal], [s].[VAT], [s].[FinalPrice], [u].[FirstName], [u].[LastName], [u].[EmailAddress]
	FROM dbo.Purchase s
	INNER JOIN dbo.[User] u ON s.StaffId = u.UserId;
END