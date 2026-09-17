-- Distributor stock feeds, managed from the admin portal.
--
-- The password is never stored in this table. SecretRef holds either ciphertext produced by
-- the Data Protection store, or the name of a secret in Key Vault — SecretProvider says which,
-- so the store can be changed per environment without a data migration.
CREATE TABLE [dbo].[DistributorFeed]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[Name] NVARCHAR(100) NOT NULL,
	[Host] NVARCHAR(200) NOT NULL,
	[Port] INT NOT NULL DEFAULT 22,
	[Username] NVARCHAR(100) NOT NULL,

	-- 'DataProtection' | 'KeyVault'
	[SecretProvider] NVARCHAR(30) NOT NULL DEFAULT 'DataProtection',
	[SecretRef] NVARCHAR(MAX) NULL,

	[RemoteDirectory] NVARCHAR(400) NOT NULL DEFAULT '.',
	-- Base64 SHA-256 of the expected host key. When set, a mismatching server is rejected.
	[HostKeySha256] NVARCHAR(200) NULL,
	[Enabled] BIT NOT NULL DEFAULT 1,

	-- Feed field mapping, so onboarding a distributor is configuration rather than code.
	[FieldSku] NVARCHAR(100) NOT NULL DEFAULT 'sku',
	[FieldName] NVARCHAR(100) NOT NULL DEFAULT 'name',
	[FieldDescription] NVARCHAR(100) NULL,
	[FieldCategory] NVARCHAR(100) NULL,
	[FieldCost] NVARCHAR(100) NULL,
	[FieldSrp] NVARCHAR(100) NULL,
	[FieldQuantity] NVARCHAR(100) NULL,
	-- Identity columns, used to resolve product content (images) from Icecat.
	[FieldManufacturer] NVARCHAR(100) NULL,
	[FieldMpn] NVARCHAR(100) NULL,
	[FieldEan] NVARCHAR(100) NULL,
	[FieldIcecat] NVARCHAR(100) NULL,

	[LastSyncedUtc] DATETIME2 NULL,
	[LastSyncStatus] NVARCHAR(400) NULL,

	-- Set while a sync of this feed is running, cleared when it finishes. The claim that stops
	-- two syncs importing one feed at once: an operator pressing Sync during the nightly
	-- window, or a second StockApi replica whose own scheduler woke at the same hour.
	--
	-- Treated as expired past a lease rather than trusted absolutely, because a process killed
	-- mid-sync leaves this set for ever and a feed that can never be claimed again is a worse
	-- failure than a double import — it is silent, where a double import is merely wasteful.
	-- See spDistributorFeed_ClaimForSync.
	[SyncStartedUtc] DATETIME2 NULL,
	[CreatedDate] DATETIME2 NOT NULL DEFAULT getutcdate(),
	[LastModified] DATETIME2 NOT NULL DEFAULT getutcdate(),

	-- Different stores buy from different distributors, so a feed belongs to a site and the
	-- sync worker loops over active sites. NOT NULL, and worth keeping that way: this row
	-- carries SecretRef, and an unscoped feed is one every tenant's admin can read the
	-- credential reference for.
	[SiteId] INT NOT NULL,

	-- Scoped by site: two stores may each have a feed called "Main".
	CONSTRAINT [UQ_DistributorFeed_Name] UNIQUE ([SiteId], [Name]),

	-- Not for uniqueness — Id is already the primary key — but so DistributorFeedSyncLog can
	-- reference (Id, SiteId) as a composite foreign key and be unable to record a sync against
	-- a store the feed does not belong to.
	CONSTRAINT [UQ_DistributorFeed_IdSite] UNIQUE ([Id], [SiteId]),
	CONSTRAINT [FK_DistributorFeed_ToSite] FOREIGN KEY ([SiteId]) REFERENCES [Site]([Id])
)
