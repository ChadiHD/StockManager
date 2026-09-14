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
	@Status nvarchar(20),
	@SiteId int = NULL
AS
BEGIN
	SET NOCOUNT ON;

	-- Resolved rather than left NULL; see dbo.fnSite_Resolve for why an unscoped row is worse
	-- than a refused one.
	SET @SiteId = [dbo].[fnSite_Resolve](@SiteId);

	IF @SiteId IS NULL
	BEGIN
		THROW 50002, 'SiteId is required: this database has more than one site, so the store to file this row under cannot be inferred.', 1;
	END

	-- Human-readable reference (AC-2041 style) derived from the identity value.
	DECLARE @Reference nvarchar(20) = CONCAT('AC-', FORMAT(NEXT VALUE FOR dbo.AccountReferenceSequence, '0000'));

	INSERT INTO dbo.Account([Reference], [Company], [ContactName], [Email], [Country], [Currency],
	                        [CustomerGroupId], [PaymentMethod], [PaymentTerms], [CreditLimit], [Status],
	                        [SiteId])
	VALUES (@Reference, @Company, @ContactName, @Email, @Country, @Currency,
	        @CustomerGroupId, @PaymentMethod, @PaymentTerms, @CreditLimit, @Status,
	        @SiteId);

	SELECT @Id = SCOPE_IDENTITY();
END
