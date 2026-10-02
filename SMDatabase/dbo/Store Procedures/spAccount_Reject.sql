-- Turns down a trading application, recording who decided, when, and why.
--
-- The reason is required rather than optional: it is what the rejection email quotes, and a
-- rejection nobody can explain is one that gets reversed by whoever fields the phone call.
CREATE PROCEDURE [dbo].[spAccount_Reject]
	@Id int,
	@ApprovedBy nvarchar(128),
	@Reason nvarchar(500),
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	IF NULLIF(LTRIM(RTRIM(@Reason)), N'') IS NULL
	BEGIN
		THROW 50007, 'A rejection needs a reason.', 1;
	END

	DECLARE @Rejected int;

	BEGIN TRY
		BEGIN TRANSACTION;

		UPDATE dbo.Account
		SET [Status] = 'Rejected',
		    -- The same two columns as an approval: this records the decision, not the approval.
		    [ApprovedUtc] = SYSUTCDATETIME(),
		    [ApprovedBy] = @ApprovedBy,
		    [RejectionReason] = @Reason
		WHERE [Id] = @Id
		  AND [SiteId] = @SiteId
		  AND [Status] IN ('Pending', 'Approved');

		SET @Rejected = @@ROWCOUNT;

		-- Told in the same transaction, for the reason spAccount_Approve gives. The reason is
		-- read back off the row rather than taken from the parameter, so the message quotes
		-- exactly what was stored.
		IF @Rejected = 1
		BEGIN
			DECLARE @Email nvarchar(256), @ContactName nvarchar(100), @Payload nvarchar(max);

			SELECT @Email = [a].[Email],
			       @ContactName = [a].[ContactName],
			       @Payload = (SELECT [a].[Company] AS [company],
			                          [a].[RejectionReason] AS [reason]
			                   FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
			FROM dbo.Account a
			WHERE [a].[Id] = @Id;

			IF NULLIF(LTRIM(RTRIM(@Email)), N'') IS NOT NULL
			BEGIN
				EXEC dbo.spEmailOutbox_Enqueue
					@SiteId = @SiteId, @ToAddress = @Email, @ToName = @ContactName,
					@TemplateKey = N'account.rejected', @PayloadJson = @Payload;
			END
		END

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		THROW;
	END CATCH

	SELECT @Rejected AS [Rejected];
END
