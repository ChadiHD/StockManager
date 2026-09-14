/*
Creates a trading application: the account, its primary contact, and its billing address, in
one transaction.

The Identity user is not created here and cannot be — it lives in ApiAuthDb and this runs in
SMDatabase. The caller creates it first and passes its id, which is why this procedure has to
be all-or-nothing: if any part of it fails the caller deletes that user again, and a partial
commit here would leave a half-application behind that no compensation can find.

@SiteId is stated by the caller rather than derived. The storefront knows which store it is
serving from the request host; this is the one registration path and it is not shared with the
admin API, so there is no case where the site is unknown.

Uniqueness is deliberately not enforced on the company. Two genuinely separate applications
from one company do happen — a second branch, or a re-application after rejection — and
refusing them here would mean a customer stuck with no way forward and a message that tells an
attacker the company is already known. The admin queue is where duplicates are noticed, by a
person who can tell them apart.

**Do not call this with INSERT ... EXEC.** It rolls back internally, and SQL Server forbids a
ROLLBACK inside an INSERT-EXEC — the real error is replaced by "Cannot use the ROLLBACK
statement within an INSERT-EXEC statement", which is true and tells you nothing about what
actually went wrong. The rollback still happens, so nothing is left behind; only the diagnosis
is lost. Dapper calls this directly and reads the grid, so the application path is unaffected;
this is a trap for a test or a batch wrapper.
*/
CREATE PROCEDURE [dbo].[spAccount_Register]
	@SiteId int,
	@IdentityUserId nvarchar(450),

	@Company nvarchar(200),
	@VatNumber nvarchar(30),
	@RegistrationNumber nvarchar(50),

	@FirstName nvarchar(100),
	@LastName nvarchar(100),
	@Email nvarchar(256),
	@Phone nvarchar(50),

	@Line1 nvarchar(200),
	@Line2 nvarchar(200),
	@City nvarchar(100),
	@Region nvarchar(100),
	@PostCode nvarchar(20),
	@Country nvarchar(2)
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @AccountId int, @ContactId int;

	/*
	Argument checks come before XACT_ABORT is switched on, and that ordering is deliberate.

	XACT_ABORT dooms whatever transaction is open, including one the caller started. A bad
	argument is the caller's mistake and it should be able to catch it, report it and carry
	on; only the part of this that writes needs the all-or-nothing treatment.
	*/
	IF NOT EXISTS (SELECT 1 FROM dbo.Site WHERE [Id] = @SiteId AND [IsActive] = 1)
	BEGIN
		THROW 50008, 'No active store with that id.', 1;
	END

	-- The caller creates the login first, so its absence here means the caller is wrong, not
	-- the applicant. Registering an account nobody can sign into is the one outcome this
	-- procedure must not produce quietly.
	IF NULLIF(LTRIM(RTRIM(@IdentityUserId)), N'') IS NULL
	BEGIN
		THROW 50009, 'An identity user is required to register an account.', 1;
	END

	-- From here on, any error aborts and rolls back — including one raised by a constraint
	-- rather than by a check. Without it a failed insert would leave the transaction open and
	-- the rows before it committed by whatever statement succeeded next.
	SET XACT_ABORT ON;

	BEGIN TRY
		BEGIN TRANSACTION;

		DECLARE @Reference nvarchar(20) =
			CONCAT('AC-', FORMAT(NEXT VALUE FOR dbo.AccountReferenceSequence, '0000'));

		-- Pending, always. Nothing a customer types decides whether they may trade; that is
		-- what the approval queue is for, and an application that could arrive Approved would
		-- be a registration form that grants credit.
		INSERT INTO dbo.Account([Reference], [Company], [ContactName], [Email], [Country],
		                        [Currency], [VatNumber], [RegistrationNumber],
		                        [PaymentMethod], [PaymentTerms], [CreditLimit], [Status], [SiteId])
		SELECT @Reference, @Company, LTRIM(RTRIM(CONCAT(@FirstName, N' ', @LastName))), @Email,
		       @Country,
		       -- The store's currency, not the applicant's country's. A store trades in one
		       -- currency and an Irish customer of a sterling store still pays in sterling.
		       [s].[CurrencyCode], @VatNumber, @RegistrationNumber,
		       N'Card', N'Prepaid', 0, N'Pending', @SiteId
		FROM dbo.Site s
		WHERE [s].[Id] = @SiteId;

		SET @AccountId = SCOPE_IDENTITY();

		INSERT INTO dbo.Contact([AccountId], [IdentityUserId], [FirstName], [LastName],
		                        [Email], [Phone], [RoleInAccount], [IsPrimary], [Status])
		VALUES (@AccountId, @IdentityUserId, @FirstName, @LastName,
		        @Email, @Phone,
		        -- The person who applies administers the account until they say otherwise:
		        -- somebody has to be able to add the second contact.
		        N'Admin', 1, N'Active');

		SET @ContactId = SCOPE_IDENTITY();

		-- Billing rather than Shipping, and default: it is the address the application was
		-- made from, and T6 decides tax treatment by comparing it with the store's country.
		-- A delivery address is added later, from the account area.
		IF NULLIF(LTRIM(RTRIM(@Line1)), N'') IS NOT NULL
		BEGIN
			INSERT INTO dbo.[Address]([AccountId], [Kind], [Line1], [Line2], [City],
			                          [Region], [PostCode], [Country], [IsDefault])
			VALUES (@AccountId, N'Billing', @Line1, @Line2, @City,
			        @Region, @PostCode, @Country, 1);
		END

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		THROW;
	END CATCH

	-- Selected rather than returned through output parameters. SaveData passes an anonymous
	-- object, which Dapper cannot write back through, so every output parameter in this
	-- database is declared and ignored. The caller reads this grid instead.
	SELECT @AccountId AS [AccountId], @ContactId AS [ContactId], @Reference AS [Reference];
END
