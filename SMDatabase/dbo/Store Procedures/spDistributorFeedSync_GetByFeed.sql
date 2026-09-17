-- The recent sync attempts for one feed, newest first.
--
-- Scoped by site as well as by feed id even though the id alone identifies the row. The
-- messages here quote a distributor's hostnames and paths back out of exception text, so this
-- is another read where a missing predicate is a disclosure rather than a display bug.
CREATE PROCEDURE [dbo].[spDistributorFeedSync_GetByFeed]
	@FeedId int,
	@SiteId int,
	@Take int = 20
AS
BEGIN
	SET NOCOUNT ON;

	-- Clamped rather than trusted: @Take reaches here from a query string, and both an
	-- unbounded page and a negative one are errors TOP would raise at the client.
	IF @Take IS NULL OR @Take < 1 SET @Take = 20;
	IF @Take > 200 SET @Take = 200;

	SELECT TOP (@Take)
	       [Id], [FeedId], [SiteId], [StartedUtc], [FinishedUtc], [Succeeded],
	       [RecordCount], [Imported], [Delisted], [Message], [TriggeredBy]
	FROM [dbo].[DistributorFeedSyncLog]
	WHERE [FeedId] = @FeedId
	  AND [SiteId] = @SiteId
	ORDER BY [StartedUtc] DESC, [Id] DESC;
END
