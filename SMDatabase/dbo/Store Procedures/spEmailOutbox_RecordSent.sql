/*
Marks a claimed message as handed to the transport.

Only while this claim still holds it. A dispatcher that stalled past its lease may already have
been overtaken; recording its answer over the newer claimant's would mark a message sent by a
send nobody can vouch for, or put back a status the other dispatcher has moved on. A rowcount
of zero says so, and the caller logs it.
*/
CREATE PROCEDURE [dbo].[spEmailOutbox_RecordSent]
	@Id int,
	@ClaimToken uniqueidentifier
AS
BEGIN
	SET NOCOUNT ON;

	UPDATE dbo.EmailOutbox
	SET [Status] = N'Sent',
	    [SentUtc] = SYSUTCDATETIME(),
	    [LastError] = NULL,
	    -- A credential that has been delivered has no further use here. See the table.
	    [PayloadJson] = CASE WHEN [PayloadProtected] = 1 THEN NULL ELSE [PayloadJson] END
	WHERE [Id] = @Id
	  AND [ClaimToken] = @ClaimToken
	  AND [Status] = N'Sending';

	SELECT @@ROWCOUNT AS [Recorded];
END
