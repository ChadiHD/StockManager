-- Takes the right to sync one feed, or reports that somebody else already holds it.
--
-- Returns Claimed = 1 when this caller may proceed and 0 when it must not. A 0 is not an error:
-- it means a sync of this feed is already running, which happens legitimately whenever an
-- operator presses Sync during the nightly window.
--
-- The whole guard is the single UPDATE. Its WHERE and its SET happen under one row lock, so two
-- callers arriving together cannot both come away with a rowcount of 1 — the second blocks, then
-- re-evaluates the predicate against the row the first just wrote and matches nothing. Reading
-- SyncStartedUtc first and updating if it looked free would be the same code with a race in the
-- gap, and the gap is exactly where a scheduled run and a button press meet.
--
-- @LeaseMinutes exists because the claim has to expire. A host killed mid-sync never clears the
-- column, and without a lease that feed would stop syncing for ever with nothing to show why.
-- An hour is far longer than a real sync of tens of thousands of records and short enough that
-- the next night recovers on its own.
CREATE PROCEDURE [dbo].[spDistributorFeed_ClaimForSync]
	@Id int,
	@SiteId int,
	@LeaseMinutes int = 60
AS
BEGIN
	SET NOCOUNT ON;

	UPDATE dbo.DistributorFeed
	SET [SyncStartedUtc] = SYSUTCDATETIME()
	WHERE [Id] = @Id
	  AND [SiteId] = @SiteId
	  AND ([SyncStartedUtc] IS NULL
	       OR [SyncStartedUtc] < DATEADD(MINUTE, -@LeaseMinutes, SYSUTCDATETIME()));

	-- Zero also covers "no such feed at this site", and deliberately does not distinguish it.
	-- A caller that has been handed a feed id it may not use should get the same refusal as one
	-- arriving a second late.
	SELECT @@ROWCOUNT AS [Claimed];
END
