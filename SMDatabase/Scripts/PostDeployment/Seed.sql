/*
Post-deployment seed. Runs on every publish, so everything here must be idempotent.

Its only job is to guarantee that a site exists. Multi-site scoping means every storefront
query filters on SiteId, and a database with no Site row renders nothing and backfills nothing.
The row seeded here is deliberately generic — a real store (aclitrade.ie and whatever follows)
is tenant configuration, inserted separately, not baked into the platform schema.

Domain 'localhost' is what SMStore sees when running under the app host, so a fresh developer
database resolves a site without any extra setup.
*/

IF NOT EXISTS (SELECT 1 FROM [dbo].[Site])
BEGIN
	INSERT INTO [dbo].[Site]
		([SiteKey], [Name], [Domain], [Country], [CurrencyCode], [Locale],
		 [OrderMode], [RegistrationFieldSet], [PriceDisplay], [IsActive])
	VALUES
		('default', 'Default store', 'localhost', 'IE', 'EUR', 'en-IE',
		 'Rfq', 'eu-b2b', 'Public', 1);
END

-- Backfill rows that predate multi-site. Only meaningful on the first deploy after the Site
-- columns land; after that these updates match nothing. Purchase is deliberately excluded —
-- a desktop POS sale has no storefront, so its SiteId stays NULL.
DECLARE @DefaultSiteId int =
	(SELECT TOP 1 [Id] FROM [dbo].[Site] ORDER BY [Id]);

UPDATE [dbo].[Account]         SET [SiteId] = @DefaultSiteId WHERE [SiteId] IS NULL;
UPDATE [dbo].[CustomerGroup]   SET [SiteId] = @DefaultSiteId WHERE [SiteId] IS NULL;
UPDATE [dbo].[Quote]           SET [SiteId] = @DefaultSiteId WHERE [SiteId] IS NULL;
UPDATE [dbo].[DistributorFeed] SET [SiteId] = @DefaultSiteId WHERE [SiteId] IS NULL;

/*
Brand aliases. Reference data rather than tenant content — a distributor's brand vocabulary
means the same thing whichever store resells it — so a starting set ships with the platform.

Only entries worth asserting are here. An unmapped brand passes through unchanged, and a wrong
alias is worse than none, so the rest are left to be added once an enrichment run shows which
brands actually miss.
*/
MERGE dbo.BrandAlias AS t
USING (VALUES
	-- FlexIT files Hewlett-Packard's consumer arm under HPINC, which matches nothing at
	-- Icecat and covers roughly two-thirds of that feed.
	(N'HPINC', N'HP', N'Feed shorthand for HP Inc.'),
	-- Not a manufacturer: the feed's bucket for unbranded accessories. NULL suppresses the
	-- brand lookup so only the EAN fallback is attempted.
	(N'UNIVERSAL', NULL, N'Not a brand — unbranded accessories.')
) AS s([FeedBrand], [IcecatBrand], [Note])
	ON t.[Distributor] IS NULL AND t.[FeedBrand] = s.[FeedBrand]
WHEN NOT MATCHED BY TARGET THEN
	INSERT ([Distributor], [FeedBrand], [IcecatBrand], [Note])
	VALUES (NULL, s.[FeedBrand], s.[IcecatBrand], s.[Note]);

-- Portal orders inherit the site of the quote or account they came from. POS rows
-- (Reference IS NULL) are left alone.
UPDATE [p]
SET [p].[SiteId] = COALESCE([q].[SiteId], [a].[SiteId])
FROM [dbo].[Purchase] p
LEFT JOIN [dbo].[Quote] q   ON q.[Id] = p.[QuoteId]
LEFT JOIN [dbo].[Account] a ON a.[Id] = p.[AccountId]
WHERE [p].[Reference] IS NOT NULL
  AND [p].[SiteId] IS NULL;

/*
Staff who existed before store access did (T9) keep the access they had, which was every store.
dbo.[User].AllSites arrives off for every row, and without this every admin of an upgraded
deployment would sign in to a portal with no store in it.

Once, by its own condition: after this runs somebody holds AllSites, and spUserSite_Set's caller
refuses to take it from the last admin who has it, so the update never matches again. A fresh
database has no users, and its first admin gets the flag from AdminBootstrap.
*/
IF NOT EXISTS (SELECT 1 FROM [dbo].[User] WHERE [AllSites] = 1)
BEGIN
	UPDATE [dbo].[User] SET [AllSites] = 1;
END

/*
Products imported before T9 recorded their feed only as its name, in Product.Distributor. Each
gets the feed that name belongs to — where exactly one feed has it — and that feed's store's
currency. A name two feeds share is ambiguous; those rows stay without a feed, which makes them
orphans no store sells, and the next sync of either feed imports its own fresh copy. Products
created after T9 carry FeedId from the merge, so the first update matches nothing again.

The second update is the invariant rather than a one-off: a feed's products are in its store's
currency. dbo.fnSite_CurrencyLocked stops a store with a feed changing currency, so it should
match nothing after the first publish.
*/
UPDATE p
SET p.[FeedId] = f.[Id]
FROM [dbo].[Product] p
INNER JOIN [dbo].[DistributorFeed] f ON f.[Name] = p.[Distributor]
WHERE p.[FeedId] IS NULL
  AND p.[Source] = N'Distributor'
  AND NOT EXISTS (SELECT 1 FROM [dbo].[DistributorFeed] other
                  WHERE other.[Name] = f.[Name] AND other.[Id] <> f.[Id]);

UPDATE p
SET p.[CurrencyCode] = s.[CurrencyCode]
FROM [dbo].[Product] p
INNER JOIN [dbo].[DistributorFeed] f ON f.[Id] = p.[FeedId]
INNER JOIN [dbo].[Site] s ON s.[Id] = f.[SiteId]
WHERE p.[CurrencyCode] <> s.[CurrencyCode];

-- Product.Published, Featured and Badge, saved aside by the pre-deployment script before the
-- schema diff dropped them, filed under each store. A no-op once moved.
:r .\MoveProductFlagsToSites.sql
GO
