-- Backs Approve / Reject on /admin/accounts, and the Suspend toggle (the caller passes the
-- status it wants; toggling between Approved and Suspended is decided in the data layer).
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
	  AND [SiteId] = @SiteId;
END
