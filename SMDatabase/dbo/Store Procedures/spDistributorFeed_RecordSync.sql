-- Records the outcome of a sync and releases the claim spDistributorFeed_ClaimForSync took.
--
-- The two happen together on purpose. A caller that recorded a status but forgot to release
-- would leave the feed unclaimable until its lease expired, which looks from the portal like a
-- sync that finished and then would not start again.
CREATE PROCEDURE [dbo].[spDistributorFeed_RecordSync]
	@Id int,
	@Status nvarchar(400),
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	UPDATE dbo.DistributorFeed
	SET [LastSyncedUtc] = SYSUTCDATETIME(),
	    [LastSyncStatus] = @Status,
	    [SyncStartedUtc] = NULL
	WHERE [Id] = @Id
	  AND [SiteId] = @SiteId;
END
