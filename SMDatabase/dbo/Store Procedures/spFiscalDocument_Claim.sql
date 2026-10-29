/*
Takes a batch of fiscal documents that are due, for one dispatcher to issue (T14).

spEmailOutbox_Claim's shape, for its reasons: one UPDATE with READPAST, a lease so that a host
killed mid-issue does not strand a document, and Attempts counted here. A document the dead host
had in fact issued may be issued twice; an issuer that can be told the order's reference as an
idempotency key should be, and the NF-e providers can.

Returns each claimed row with its order's reference, which the dispatcher reads the order by.
*/
CREATE PROCEDURE [dbo].[spFiscalDocument_Claim]
	@ClaimToken uniqueidentifier,
	@BatchSize int = 10,
	@LeaseMinutes int = 10
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @Claimed TABLE ([Id] int, [SiteId] int, [PurchaseId] int, [Issuer] nvarchar(40),
	                        [Attempts] int, [ClaimToken] uniqueidentifier);

	WITH [due] AS (
		SELECT TOP (@BatchSize) *
		FROM dbo.FiscalDocument WITH (ROWLOCK, UPDLOCK, READPAST)
		WHERE ([Status] = N'Pending' AND [NextAttemptUtc] <= SYSUTCDATETIME())
		   OR ([Status] = N'Issuing'
		       AND [ClaimedUtc] < DATEADD(MINUTE, -@LeaseMinutes, SYSUTCDATETIME()))
		ORDER BY [NextAttemptUtc], [Id])
	UPDATE [due]
	SET [Status] = N'Issuing',
	    [ClaimedUtc] = SYSUTCDATETIME(),
	    [ClaimToken] = @ClaimToken,
	    [Attempts] = [Attempts] + 1
	OUTPUT [inserted].[Id], [inserted].[SiteId], [inserted].[PurchaseId], [inserted].[Issuer],
	       [inserted].[Attempts], [inserted].[ClaimToken]
	INTO @Claimed;

	SELECT c.[Id], c.[SiteId], c.[PurchaseId], p.[Reference], c.[Issuer], c.[Attempts], c.[ClaimToken]
	FROM @Claimed c
	INNER JOIN dbo.Purchase p ON p.[Id] = c.[PurchaseId] AND p.[SiteId] = c.[SiteId];
END
