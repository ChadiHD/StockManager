-- Creates a sales order (a Purchase row carrying a Reference). The desktop POS keeps using
-- spPurchase_Insert, which leaves Reference/Account/Status NULL.
CREATE PROCEDURE [dbo].[spOrder_Insert]
	@Id int output,
	@Reference nvarchar(20) output,
	@StaffId nvarchar(128),
	@AccountId int,
	@Currency nvarchar(3),
	@QuoteId int = NULL,
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	-- The account has to belong to the store the caller is acting for, or an admin could raise
	-- an order against another tenant's customer by guessing an id.
	IF NOT EXISTS (SELECT 1 FROM dbo.Account WHERE [Id] = @AccountId AND [SiteId] = @SiteId)
	BEGIN
		THROW 50005, 'That account does not belong to this store.', 1;
	END

	IF @QuoteId IS NOT NULL
	   AND NOT EXISTS (SELECT 1 FROM dbo.Quote WHERE [Id] = @QuoteId AND [SiteId] = @SiteId)
	BEGIN
		THROW 50004, 'That quote does not belong to this store.', 1;
	END

	SET @Reference = CONCAT('SO-', FORMAT(NEXT VALUE FOR dbo.OrderReferenceSequence, '0000'));

	INSERT INTO dbo.Purchase([StaffId], [PurchaseDate], [SubTotal], [VAT], [FinalPrice],
	                         [Reference], [AccountId], [QuoteId], [Currency], [Status], [SiteId])
	VALUES (@StaffId, SYSUTCDATETIME(), 0, 0, 0,
	        @Reference, @AccountId, @QuoteId, @Currency, 'Awaiting payment', @SiteId);

	SELECT @Id = SCOPE_IDENTITY();
END
