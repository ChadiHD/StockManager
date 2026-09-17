-- The recent sync attempts across every feed in one store, newest first.
--
-- What the portal's feeds page shows above the list: whether last night happened at all, and
-- for which feeds. Per-feed detail is spDistributorFeedSync_GetByFeed.
CREATE PROCEDURE [dbo].[spDistributorFeedSync_GetRecent]
	@SiteId int,
	@Take int = 20
AS
BEGIN
	SET NOCOUNT ON;

	IF @Take IS NULL OR @Take < 1 SET @Take = 20;
	IF @Take > 200 SET @Take = 200;

	SELECT TOP (@Take)
	       [l].[Id], [l].[FeedId], [l].[SiteId], [l].[StartedUtc], [l].[FinishedUtc],
	       [l].[Succeeded], [l].[RecordCount], [l].[Imported], [l].[Delisted],
	       [l].[Message], [l].[TriggeredBy],
	       -- Joined so the page can name the feed without holding the whole feed list, which
	       -- carries SecretRef and is a heavier read than this one.
	       [f].[Name] AS [FeedName]
	FROM [dbo].[DistributorFeedSyncLog] l
	INNER JOIN [dbo].[DistributorFeed] f
		ON f.[Id] = l.[FeedId]
		AND f.[SiteId] = l.[SiteId]
	WHERE [l].[SiteId] = @SiteId
	ORDER BY [l].[StartedUtc] DESC, [l].[Id] DESC;
END
