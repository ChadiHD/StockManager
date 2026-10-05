/*
Approves a trading application: records who decided and when, assigns the pricing group, and
opens the account for sign-in.

Replaces calling spAccount_UpdateStatus with 'Approved'. That still exists for suspending and
reinstating, but it cannot be the approval path, because approval is the one transition that
has to leave evidence — a customer will eventually ask why they were let in on these terms,
and "someone changed a status" is not an answer.

Only a Pending or Rejected account can be approved. An already-Approved one is left alone
rather than having its approver and timestamp overwritten by whoever opened the screen next.
*/
CREATE PROCEDURE [dbo].[spAccount_Approve]
	@Id int,
	@ApprovedBy nvarchar(128),
	@CustomerGroupId int,
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	-- Assigning another store's group would price this customer from a rate card this
	-- store's operator cannot see.
	IF @CustomerGroupId IS NOT NULL
	   AND NOT EXISTS (SELECT 1 FROM dbo.CustomerGroup
	                   WHERE [Id] = @CustomerGroupId AND [SiteId] = @SiteId)
	BEGIN
		THROW 50003, 'That customer group does not belong to this store.', 1;
	END

	DECLARE @Approved int;

	BEGIN TRY
		BEGIN TRANSACTION;

		UPDATE dbo.Account
		SET [Status] = 'Approved',
		    [ApprovedUtc] = SYSUTCDATETIME(),
		    [ApprovedBy] = @ApprovedBy,
		    -- Cleared: a rejection reason left behind an approval would be quoted back by the
		    -- next email that renders it.
		    [RejectionReason] = NULL,
		    [CustomerGroupId] = COALESCE(@CustomerGroupId, [CustomerGroupId])
		WHERE [Id] = @Id
		  AND [SiteId] = @SiteId
		  AND [Status] IN ('Pending', 'Rejected');

		SET @Approved = @@ROWCOUNT;

		/*
		The applicant is told in the same transaction that lets them in.

		Before T6 the controller sent this after the commit, and a host that died in between
		approved an account whose owner was never told -- they would wait, ring, and be told
		it had been open for a week. Here the decision and the message commit together. Only
		on a real transition: a no-op means somebody else decided first, and they sent theirs.

		An account with no address gets no row rather than one that can only dead-letter.
		Registration always records one; an account keyed in by hand may not, and the
		controller logs that case.
		*/
		IF @Approved = 1
		BEGIN
			DECLARE @Email nvarchar(256), @ContactName nvarchar(100), @Payload nvarchar(max);

			SELECT @Email = [a].[Email],
			       @ContactName = [a].[ContactName],
			       @Payload = (SELECT [a].[Company] AS [company]
			                   FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
			FROM dbo.Account a
			WHERE [a].[Id] = @Id;

			IF NULLIF(LTRIM(RTRIM(@Email)), N'') IS NOT NULL
			BEGIN
				EXEC dbo.spEmailOutbox_Enqueue
					@SiteId = @SiteId, @ToAddress = @Email, @ToName = @ContactName,
					@TemplateKey = N'account.approved', @PayloadJson = @Payload;
			END
		END

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		THROW;
	END CATCH

	SELECT @Approved AS [Approved];
END
