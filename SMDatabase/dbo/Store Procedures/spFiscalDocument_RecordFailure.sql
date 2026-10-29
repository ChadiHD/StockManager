/*
Records a failed attempt to issue (T14): when to try again, or, with @RetryAfterSeconds NULL, that
there will be no more tries until somebody calls spFiscalDocument_Retry. The schedule is
OutboxRetryPolicy's, in C#. Only while this claim still holds the row. Selects 1 when recorded.
*/
CREATE PROCEDURE [dbo].[spFiscalDocument_RecordFailure]
	@Id int,
	@ClaimToken uniqueidentifier,
	@Error nvarchar(1000),
	@RetryAfterSeconds int = NULL
AS
BEGIN
	SET NOCOUNT ON;

	UPDATE dbo.FiscalDocument
	SET [Status] = CASE WHEN @RetryAfterSeconds IS NULL THEN N'DeadLettered' ELSE N'Pending' END,
	    [NextAttemptUtc] = CASE WHEN @RetryAfterSeconds IS NULL THEN [NextAttemptUtc]
	                            ELSE DATEADD(SECOND, @RetryAfterSeconds, SYSUTCDATETIME()) END,
	    [LastError] = @Error
	WHERE [Id] = @Id
	  AND [ClaimToken] = @ClaimToken
	  AND [Status] = N'Issuing';

	SELECT @@ROWCOUNT AS [Recorded];
END
