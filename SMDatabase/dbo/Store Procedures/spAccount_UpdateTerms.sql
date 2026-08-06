CREATE PROCEDURE [dbo].[spAccount_UpdateTerms]
	@Id int,
	@CustomerGroupId int,
	@PaymentMethod nvarchar(50),
	@PaymentTerms nvarchar(50),
	@CreditLimit money
AS
BEGIN
	SET NOCOUNT ON;

	UPDATE dbo.Account
	SET [CustomerGroupId] = @CustomerGroupId,
	    [PaymentMethod] = @PaymentMethod,
	    [PaymentTerms] = @PaymentTerms,
	    [CreditLimit] = @CreditLimit
	WHERE [Id] = @Id;
END
