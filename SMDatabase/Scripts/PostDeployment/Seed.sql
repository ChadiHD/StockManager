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

-- Portal orders inherit the site of the quote or account they came from. POS rows
-- (Reference IS NULL) are left alone.
UPDATE [p]
SET [p].[SiteId] = COALESCE([q].[SiteId], [a].[SiteId])
FROM [dbo].[Purchase] p
LEFT JOIN [dbo].[Quote] q   ON q.[Id] = p.[QuoteId]
LEFT JOIN [dbo].[Account] a ON a.[Id] = p.[AccountId]
WHERE [p].[Reference] IS NOT NULL
  AND [p].[SiteId] IS NULL;
