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

	UPDATE dbo.Account
	SET [Status] = 'Rejected',
	    -- The same two columns as an approval: this records the decision, not the approval.
	    [ApprovedUtc] = SYSUTCDATETIME(),
	    [ApprovedBy] = @ApprovedBy,
	    [RejectionReason] = @Reason
	WHERE [Id] = @Id
	  AND [SiteId] = @SiteId
	  AND [Status] IN ('Pending', 'Approved');

	SELECT @@ROWCOUNT AS [Rejected];
END
