/*
Puts a dead-lettered fiscal document back in the queue, from the admin's order screen (T14), with
its attempts reset. A mail given up on can be shrugged off; an invoice the law requires cannot,
so once somebody has fixed what the issuer refused, it goes again.

Only a dead letter, and only the acting store's: selects 0 for anything else, which the screen
reads as "somebody already did".
*/
CREATE PROCEDURE [dbo].[spFiscalDocument_Retry]
	@SiteId int,
	@Reference nvarchar(20)
AS
BEGIN
	SET NOCOUNT ON;

	UPDATE f
	SET [Status] = N'Pending',
	    [Attempts] = 0,
	    [NextAttemptUtc] = SYSUTCDATETIME(),
	    [ClaimToken] = NULL
	FROM dbo.FiscalDocument f
	INNER JOIN dbo.Purchase p ON p.[Id] = f.[PurchaseId] AND p.[SiteId] = @SiteId
	WHERE f.[SiteId] = @SiteId
	  AND p.[Reference] = @Reference
	  AND f.[Status] = N'DeadLettered';

	SELECT @@ROWCOUNT AS [Retried];
END
