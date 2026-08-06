CREATE PROCEDURE [dbo].[spAccount_Insert]
	@Id int output,
	@Company nvarchar(200),
	@ContactName nvarchar(100),
	@Email nvarchar(256),
	@Country nvarchar(100),
	@Currency nvarchar(3),
	@CustomerGroupId int,
	@PaymentMethod nvarchar(50),
	@PaymentTerms nvarchar(50),
	@CreditLimit money,
	@Status nvarchar(20)
AS
BEGIN
	SET NOCOUNT ON;

	-- Human-readable reference (AC-2041 style) derived from the identity value.
	DECLARE @Reference nvarchar(20) = CONCAT('AC-', FORMAT(NEXT VALUE FOR dbo.AccountReferenceSequence, '0000'));

	INSERT INTO dbo.Account([Reference], [Company], [ContactName], [Email], [Country], [Currency],
	                        [CustomerGroupId], [PaymentMethod], [PaymentTerms], [CreditLimit], [Status])
	VALUES (@Reference, @Company, @ContactName, @Email, @Country, @Currency,
	        @CustomerGroupId, @PaymentMethod, @PaymentTerms, @CreditLimit, @Status);

	SELECT @Id = SCOPE_IDENTITY();
END
