-- Creates a sales order (a Purchase row carrying a Reference). The desktop POS keeps using
-- spPurchase_Insert, which leaves Reference/Account/Status NULL.
CREATE PROCEDURE [dbo].[spOrder_Insert]
	@Id int output,
	@Reference nvarchar(20) output,
	@StaffId nvarchar(128),
	@AccountId int,
	@Currency nvarchar(3),
	@QuoteId int = NULL
AS
BEGIN
	SET NOCOUNT ON;

	SET @Reference = CONCAT('SO-', FORMAT(NEXT VALUE FOR dbo.OrderReferenceSequence, '0000'));

	INSERT INTO dbo.Purchase([StaffId], [PurchaseDate], [SubTotal], [VAT], [FinalPrice],
	                         [Reference], [AccountId], [QuoteId], [Currency], [Status])
	VALUES (@StaffId, SYSUTCDATETIME(), 0, 0, 0,
	        @Reference, @AccountId, @QuoteId, @Currency, 'Awaiting payment');

	SELECT @Id = SCOPE_IDENTITY();
END
