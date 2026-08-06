-- Applies a whole distributor feed in one call: upserts everything in @Items, then delists
-- anything this distributor previously supplied that is no longer in the feed.
--
-- Replaces the per-row spProduct_UpsertFromFeed, which needed one round trip per product and
-- did not survive a mid-import failure. Wrapped in a transaction so a feed either lands
-- completely or not at all.
--
-- RetailPrice is only ever set on INSERT (seeded from the distributor's SRP). Once a product
-- exists the sell price belongs to the operator and a re-sync must not overwrite it.
CREATE PROCEDURE [dbo].[spProduct_BulkUpsertFromFeed]
	@Distributor nvarchar(100),
	@Items dbo.DistributorFeedItem READONLY
AS
BEGIN
	SET NOCOUNT ON;
	SET XACT_ABORT ON;

	DECLARE @Now datetime2 = SYSUTCDATETIME();

	BEGIN TRY
		BEGIN TRANSACTION;

		MERGE dbo.Product AS target
		USING @Items AS source
			ON target.[Distributor] = @Distributor
			AND target.[DistributorSku] = source.[DistributorSku]
		WHEN MATCHED THEN
			UPDATE SET
				target.[ProductName] = source.[ProductName],
				target.[Description] = ISNULL(source.[Description], target.[Description]),
				target.[Category] = ISNULL(source.[Category], target.[Category]),
				target.[Cost] = source.[Cost],
				target.[QuantityInStock] = source.[QuantityInStock],
				target.[Sku] = ISNULL(target.[Sku], source.[Sku]),
				target.[Source] = 'Distributor',
				target.[Delisted] = 0,
				target.[LastSynced] = @Now,
				target.[LastModified] = @Now
		WHEN NOT MATCHED BY TARGET THEN
			INSERT ([ProductName], [Description], [RetailPrice], [QuantityInStock], [IsTaxable],
			        [Sku], [Category], [Cost], [Source], [Distributor], [DistributorSku],
			        [LastSynced], [Delisted])
			VALUES (source.[ProductName], ISNULL(source.[Description], N''), ISNULL(source.[Srp], 0),
			        source.[QuantityInStock], 1, source.[Sku], source.[Category], source.[Cost],
			        'Distributor', @Distributor, source.[DistributorSku], @Now, 0);

		-- Anything this distributor used to supply but no longer lists: flag it and zero the
		-- stock so it cannot be sold, without deleting rows that quotes may reference.
		UPDATE p
		SET p.[Delisted] = 1,
		    p.[QuantityInStock] = 0,
		    p.[LastModified] = @Now
		FROM dbo.Product p
		WHERE p.[Distributor] = @Distributor
		  AND p.[Delisted] = 0
		  AND NOT EXISTS (SELECT 1 FROM @Items i WHERE i.[DistributorSku] = p.[DistributorSku]);

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		THROW;
	END CATCH

	SELECT
		(SELECT COUNT(*) FROM @Items) AS [Received],
		(SELECT COUNT(*) FROM dbo.Product WHERE [Distributor] = @Distributor AND [Delisted] = 1) AS [Delisted];
END
