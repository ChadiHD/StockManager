-- Backs Approve / Reject on /admin/accounts, and the Suspend toggle (the caller passes the
-- status it wants; toggling between Approved and Suspended is decided in the data layer).
CREATE PROCEDURE [dbo].[spAccount_UpdateStatus]
	@Id int,
	@Status nvarchar(20)
AS
BEGIN
	SET NOCOUNT ON;

	UPDATE dbo.Account
	SET [Status] = @Status
	WHERE [Id] = @Id;
END
