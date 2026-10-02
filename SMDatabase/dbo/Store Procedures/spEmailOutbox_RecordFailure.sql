/*
Records a failed attempt: either when to try again, or that there will be no more tries.

@RetryAfterSeconds NULL means dead-letter. The decision is OutboxRetryPolicy's, in C#, where
the schedule is a pure function the tests can drive without a database; this procedure only
writes it down.

The claim token is checked exactly as spEmailOutbox_RecordSent checks it, for the same reason.
*/
CREATE PROCEDURE [dbo].[spEmailOutbox_RecordFailure]
	@Id int,
	@ClaimToken uniqueidentifier,
	@Error nvarchar(1000),
	@RetryAfterSeconds int = NULL
AS
BEGIN
	SET NOCOUNT ON;

	UPDATE dbo.EmailOutbox
	SET [Status] = CASE WHEN @RetryAfterSeconds IS NULL THEN N'DeadLettered' ELSE N'Pending' END,
	    [NextAttemptUtc] = CASE WHEN @RetryAfterSeconds IS NULL THEN [NextAttemptUtc]
	                            ELSE DATEADD(SECOND, @RetryAfterSeconds, SYSUTCDATETIME()) END,
	    [LastError] = @Error,
	    -- Given up on: a credential nobody will deliver is a credential nobody needs kept.
	    -- The customer asks for another link, which is a fresh row.
	    [PayloadJson] = CASE WHEN @RetryAfterSeconds IS NULL AND [PayloadProtected] = 1
	                         THEN NULL ELSE [PayloadJson] END
	WHERE [Id] = @Id
	  AND [ClaimToken] = @ClaimToken
	  AND [Status] = N'Sending';

	SELECT @@ROWCOUNT AS [Recorded];
END
