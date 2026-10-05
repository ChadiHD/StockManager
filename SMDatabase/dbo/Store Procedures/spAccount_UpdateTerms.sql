CREATE PROCEDURE [dbo].[spAccount_UpdateTerms]
	@Id int,
	@CustomerGroupId int,
	@PaymentMethod nvarchar(50),
	@PaymentTerms nvarchar(50),
	-- Sent rather than parsed from the label: the portal offers a fixed list and therefore
	-- knows the number. CK_Account_Terms refuses a pair that disagrees, so a caller that
	-- sends one without the other fails at the write instead of granting terms nobody chose.
	@PaymentTermsDays int,
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
	    [PaymentTermsDays] = @PaymentTermsDays,
	    [CreditLimit] = @CreditLimit
	WHERE [Id] = @Id
	  AND [SiteId] = @SiteId;
END
