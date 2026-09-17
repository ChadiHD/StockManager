-- The desktop POS's own sales report, by staff member.
CREATE PROCEDURE [dbo].[spPurchase_PurchaseReport]
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [s].[PurchaseDate], [s].[SubTotal], [s].[VAT], [s].[FinalPrice], [u].[FirstName], [u].[LastName], [u].[EmailAddress]
	FROM dbo.Purchase s
	INNER JOIN dbo.[User] u ON s.StaffId = u.UserId
	-- POS sales only, which is the other half of a discipline that until T5 held on one side
	-- only: every spOrder_* procedure filters Reference IS NOT NULL, and this one filtered
	-- nothing, so portal orders were reported here too — attributed to whichever admin
	-- converted them.
	--
	-- Stating it matters more since StaffId became nullable. A customer-accepted order has no
	-- staff row, so the inner join above would now drop it from this report: the right answer
	-- for a POS report, reached by accident. This makes it the intent.
	WHERE [s].[Reference] IS NULL;
END
