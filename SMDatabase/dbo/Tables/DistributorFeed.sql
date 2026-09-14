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
	[CreatedDate] DATETIME2 NOT NULL DEFAULT getutcdate(),
	[LastModified] DATETIME2 NOT NULL DEFAULT getutcdate(),

	-- Different stores buy from different distributors, so a feed belongs to a site and the
	-- sync worker loops over active sites. NOT NULL, and worth keeping that way: this row
	-- carries SecretRef, and an unscoped feed is one every tenant's admin can read the
	-- credential reference for.
	[SiteId] INT NOT NULL,

	-- Scoped by site: two stores may each have a feed called "Main".
	CONSTRAINT [UQ_DistributorFeed_Name] UNIQUE ([SiteId], [Name]),
	CONSTRAINT [FK_DistributorFeed_ToSite] FOREIGN KEY ([SiteId]) REFERENCES [Site]([Id])
)
