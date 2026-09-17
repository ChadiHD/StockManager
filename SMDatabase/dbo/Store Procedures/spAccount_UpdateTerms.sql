CREATE PROCEDURE [dbo].[spAccount_UpdateTerms]
	@Id int,
	@CustomerGroupId int,
	@PaymentMethod nvarchar(50),
	@PaymentTerms nvarchar(50),
	@CreditLimit money,
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	-- A group from another store would set this account's discount from a rate the operator
	-- cannot see, so the assignment is rejected rather than silently dropped.
	IF @CustomerGroupId IS NOT NULL
	   AND NOT EXISTS (SELECT 1 FROM dbo.CustomerGroup
	                   WHERE [Id] = @CustomerGroupId AND [SiteId] = @SiteId)
	BEGIN
		THROW 50003, 'That customer group does not belong to this store.', 1;
	END

	UPDATE dbo.Account
	SET [CustomerGroupId] = @CustomerGroupId,
	    [PaymentMethod] = @PaymentMethod,
	    [PaymentTerms] = @PaymentTerms,
	    [CreditLimit] = @CreditLimit
	WHERE [Id] = @Id
	  AND [SiteId] = @SiteId;
END
