-- Backs the "Sync distributor feeds" action on /admin/products. Marks every distributor-sourced
-- product as freshly synced; a real feed integration would replace the body, not the contract.
CREATE PROCEDURE [dbo].[spProduct_SyncFeeds]
AS
BEGIN
	SET NOCOUNT ON;

	UPDATE dbo.Product
	SET [LastSynced] = SYSUTCDATETIME()
	WHERE [Source] = 'Distributor';

	SELECT @@ROWCOUNT AS [SyncedCount];
END
