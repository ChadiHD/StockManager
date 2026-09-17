-- Enabled feeds this store has not heard from within its own staleness threshold.
--
-- A different question from "did the last sync fail". A feed can be perfectly healthy and
-- simply never attempted — the scheduler off, the host down for two days, a misconfigured sync
-- time — and nothing in DistributorFeedSyncLog would say so, because no attempt was made. This
-- is the condition nobody is told about any other way.
--
-- A feed that has never synced counts as stale. There is no attempt to distinguish "brand new,
-- give it until tonight" from "broken since it was created": both mean the catalog carries no
-- confirmed stock from this distributor, and the operator's next move is the same.
--
-- Disabled feeds are excluded. An operator who turned a feed off does not need telling that it
-- stopped delivering.
CREATE PROCEDURE [dbo].[spDistributorFeed_GetStale]
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	-- Resolved once into a variable rather than compared per row, and NULL when the store has
	-- no threshold — in which case nothing below matches and no alert is possible. Note this is
	-- FeedStaleAfterHours alone: HideStaleProducts decides whether stale stock leaves the
	-- storefront, which is a separate decision from whether anybody is told about it. A store
	-- that wants to keep selling while it chases the distributor still wants the mail.
	DECLARE @StaleBeforeUtc datetime2 = CASE
		WHEN (SELECT [FeedStaleAfterHours] FROM dbo.Site WHERE [Id] = @SiteId) > 0
		THEN DATEADD(HOUR,
		             -(SELECT [FeedStaleAfterHours] FROM dbo.Site WHERE [Id] = @SiteId),
		             SYSUTCDATETIME())
	END;

	SELECT [Id], [SiteId], [Name], [LastSyncedUtc], [LastSyncStatus], [SyncStartedUtc]
	FROM dbo.DistributorFeed
	WHERE [SiteId] = @SiteId
	  AND [Enabled] = 1
	  AND @StaleBeforeUtc IS NOT NULL
	  AND ([LastSyncedUtc] IS NULL OR [LastSyncedUtc] < @StaleBeforeUtc)
	ORDER BY [Name];
END
