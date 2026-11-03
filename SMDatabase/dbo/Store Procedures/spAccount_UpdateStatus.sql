-- Backs the Suspend toggle on /admin/accounts: Approved <-> Suspended and nothing else.
-- Approval and rejection are spAccount_Approve and spAccount_Reject, which record who decided
-- and queue the applicant's mail; reaching Approved from Pending here would do neither.
CREATE PROCEDURE [dbo].[spAccount_UpdateStatus]
	@Id int,
	@Status nvarchar(20),
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	-- Scoped like the reads. A mutation reached by a guessed id is the half of tenant
	-- isolation that is easy to forget, and the more damaging half.
	UPDATE dbo.Account
	SET [Status] = @Status
	WHERE [Id] = @Id
	  AND [SiteId] = @SiteId
	  AND @Status IN ('Approved', 'Suspended')
	  AND [Status] IN ('Approved', 'Suspended');
END
