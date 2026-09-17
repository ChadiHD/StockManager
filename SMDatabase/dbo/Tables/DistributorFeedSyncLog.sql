-- One row per attempt to sync a distributor feed, successful or not.
--
-- DistributorFeed.LastSyncStatus holds one 400-character string and the next run overwrites it,
-- so a feed that has failed every night for a week is indistinguishable from one that failed
-- once an hour ago. This is the history that makes "it has been broken since Tuesday"
-- answerable, and it is what the failure alert reads to decide whether a failure is new.
--
-- No pruning, deliberately. Four feeds syncing nightly write about 1,500 rows a year, and a
-- retention job would be more moving parts than the thing it maintains.
CREATE TABLE [dbo].[DistributorFeedSyncLog]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[FeedId] INT NOT NULL,

	-- Denormalised from the feed rather than joined for, because every read of this table is
	-- scoped to a store and the composite foreign key below makes the two impossible to
	-- disagree. It also means a site-scoped read needs no join at all.
	[SiteId] INT NOT NULL,

	[StartedUtc] DATETIME2 NOT NULL,
	[FinishedUtc] DATETIME2 NOT NULL,
	[Succeeded] BIT NOT NULL,

	[RecordCount] INT NOT NULL DEFAULT 0,
	[Imported] INT NOT NULL DEFAULT 0,
	[Delisted] INT NOT NULL DEFAULT 0,

	-- The same text LastSyncStatus gets: the outcome, or a trimmed exception message. Never a
	-- whole exception — that can carry a hostname, a path and a username, and this table is
	-- read by the portal.
	[Message] NVARCHAR(400) NULL,

	-- 'Schedule' | 'Operator'. Worth a column of its own: a feed that only ever succeeds when
	-- somebody presses the button is a scheduler problem rather than a feed problem, and
	-- nothing else here would tell the two apart.
	[TriggeredBy] NVARCHAR(20) NOT NULL DEFAULT 'Operator',

	-- Composite, so a log row cannot claim a site its feed does not belong to. Requires the
	-- matching unique constraint on DistributorFeed; see CLAUDE.md on preferring a composite
	-- key wherever a scoped row references another scoped row.
	CONSTRAINT [FK_DistributorFeedSyncLog_ToFeed] FOREIGN KEY ([FeedId], [SiteId])
		REFERENCES [DistributorFeed]([Id], [SiteId]) ON DELETE CASCADE
)
GO

-- Every read is "the newest attempts for this feed", so the index carries the order.
CREATE NONCLUSTERED INDEX [IX_DistributorFeedSyncLog_Feed]
	ON [dbo].[DistributorFeedSyncLog] ([FeedId], [StartedUtc] DESC)
GO

-- And "the newest attempts anywhere in this store", for the portal's own overview.
CREATE NONCLUSTERED INDEX [IX_DistributorFeedSyncLog_Site]
	ON [dbo].[DistributorFeedSyncLog] ([SiteId], [StartedUtc] DESC)
