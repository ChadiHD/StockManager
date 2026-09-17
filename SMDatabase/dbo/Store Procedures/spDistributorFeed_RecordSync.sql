-- Records the outcome of a sync: the feed's own last-run fields, a row in the history, and the
-- release of the claim spDistributorFeed_ClaimForSync took.
--
-- All three in one call, and in one transaction, because a caller that did two of them would
-- leave a state nobody can read correctly. A status written without releasing looks from the
-- portal like a sync that finished and then would not start again; a release without a history
-- row loses the evidence the failure alert uses to tell a new failure from a continuing one.
CREATE PROCEDURE [dbo].[spDistributorFeed_RecordSync]
	@Id int,
	@Status nvarchar(400),
	@SiteId int,
	@Succeeded bit,
	@StartedUtc datetime2,
	@RecordCount int = 0,
	@Imported int = 0,
	@Delisted int = 0,
	@TriggeredBy nvarchar(20) = 'Operator'
AS
BEGIN
	SET NOCOUNT ON;
	SET XACT_ABORT ON;

	DECLARE @Now datetime2 = SYSUTCDATETIME();

	BEGIN TRY
		BEGIN TRANSACTION;

		UPDATE dbo.DistributorFeed
		SET [LastSyncedUtc] = @Now,
		    [LastSyncStatus] = @Status,
		    [SyncStartedUtc] = NULL
		WHERE [Id] = @Id
		  AND [SiteId] = @SiteId;

		-- Only when the feed is this store's. Without the guard, a mismatched pair would fail
		-- on the composite foreign key instead — correct, but as an error rather than as the
		-- silence a caller acting on a feed it may not touch deserves.
		IF @@ROWCOUNT = 1
		BEGIN
			INSERT INTO dbo.DistributorFeedSyncLog
				([FeedId], [SiteId], [StartedUtc], [FinishedUtc], [Succeeded],
				 [RecordCount], [Imported], [Delisted], [Message], [TriggeredBy])
			VALUES
				(@Id, @SiteId, @StartedUtc, @Now, @Succeeded,
				 @RecordCount, @Imported, @Delisted, @Status, @TriggeredBy);
		END

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		THROW;
	END CATCH
END
