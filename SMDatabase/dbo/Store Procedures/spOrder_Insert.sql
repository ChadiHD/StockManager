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

	-- A portal order inherits the site of the quote or account it came from, the same order of
	-- preference the post-deployment backfill uses. A POS sale has no site and never reaches
	-- this procedure.
	DECLARE @SiteId int = [dbo].[fnSite_Resolve](COALESCE(
		(SELECT [SiteId] FROM dbo.Quote   WHERE [Id] = @QuoteId),
		(SELECT [SiteId] FROM dbo.Account WHERE [Id] = @AccountId)));

	IF @SiteId IS NULL
	BEGIN
		THROW 50002, 'SiteId is required: neither the quote nor the account has a site and this database has more than one, so the store to file this order under cannot be inferred.', 1;
	END

	SET @Reference = CONCAT('SO-', FORMAT(NEXT VALUE FOR dbo.OrderReferenceSequence, '0000'));

	INSERT INTO dbo.Purchase([StaffId], [PurchaseDate], [SubTotal], [VAT], [FinalPrice],
	                         [Reference], [AccountId], [QuoteId], [Currency], [Status], [SiteId])
	VALUES (@StaffId, SYSUTCDATETIME(), 0, 0, 0,
	        @Reference, @AccountId, @QuoteId, @Currency, 'Awaiting payment', @SiteId);

	SELECT @Id = SCOPE_IDENTITY();
END
