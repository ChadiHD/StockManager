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

	SELECT @@ROWCOUNT AS [Approved];
END
