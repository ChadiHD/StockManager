-- Maps a raw feed category string onto one of a store's own categories.
--
-- This table also decides what a store sells. dbo.Product is shared — the desktop POS and own
-- warehouse stock have no site, and one physical product may legitimately be sold by two
-- stores — so a product is not scoped by a SiteId column. It is in a store's catalog when its
-- feed category has a mapping row for that store, and absent when it does not. Onboarding a
-- distributor is therefore mapping work, and a feed value nobody has mapped stays invisible
-- rather than appearing uncategorised in a live storefront.
CREATE TABLE [dbo].[CategoryMapping]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[SiteId] INT NOT NULL,

	-- Matched against dbo.Product.Category exactly. Feed values are stable strings, not free
	-- text, so exact matching is right and a changed value should surface as unmapped.
	[FeedValue] NVARCHAR(100) NOT NULL,

	[SiteCategoryId] INT NOT NULL,
	[CreatedDate] DATETIME2 NOT NULL DEFAULT getutcdate(),

	CONSTRAINT [FK_CategoryMapping_ToSite] FOREIGN KEY ([SiteId]) REFERENCES [Site]([Id]),
	-- Composite on purpose. Keyed on SiteCategoryId alone, this permits a row for store A that
	-- points at store B's category — and since mapping rows decide what a store sells, that is
	-- how one tenant's taxonomy surfaces on another tenant's storefront. Carrying SiteId into
	-- the reference makes the mismatch impossible to insert rather than something every query
	-- has to remember to filter out.
	CONSTRAINT [FK_CategoryMapping_ToSiteCategory] FOREIGN KEY ([SiteCategoryId], [SiteId])
		REFERENCES [SiteCategory]([Id], [SiteId]),
	-- One destination per feed value per store, so a product lands in exactly one category.
	CONSTRAINT [UQ_CategoryMapping_FeedValue] UNIQUE ([SiteId], [FeedValue])
)
GO

-- The catalog query joins Product.Category to this on every request, so the lookup direction
-- gets its own index.
CREATE NONCLUSTERED INDEX [IX_CategoryMapping_SiteFeedValue]
	ON [dbo].[CategoryMapping] ([SiteId], [FeedValue])
	INCLUDE ([SiteCategoryId]);
