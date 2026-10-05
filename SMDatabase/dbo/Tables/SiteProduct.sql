/*
One store's decisions about one product: whether it sells it, where it files it, and how it
features it.

dbo.Product is shared -- the desktop POS has no site, and one physical product may be sold by
two stores -- so anything a store decides about a product cannot live on it. Published,
Featured and Badge did, until T8: unpublishing a product for one store took it off every store,
and one store's "Best seller" ribbon appeared on another's tile.

Exceptions only. The category mapping is still what puts a product on a store; a row here is
needed only when the store wants something other than what its mapping says. An allow-list --
a row per product the store sells -- was the obvious alternative and the wrong one for a
catalog fed nightly: every new SKU would be invisible until somebody ticked it.

Visibility:
  NULL    follow the category mapping
  'Hide'  not on this store, whatever the mapping says
  'Show'  on this store; under SiteCategoryId when given, otherwise where the mapping files it

Read through dbo.fnSite_ProductPlacement and nowhere else, so the storefront and the admin
screen cannot disagree about what a store sells.
*/
CREATE TABLE [dbo].[SiteProduct]
(
	[SiteId] INT NOT NULL,
	[ProductId] INT NOT NULL,
	[Visibility] NVARCHAR(10) NULL,

	-- Where a shown product is filed, when its feed category is unmapped or the store wants it
	-- somewhere else. Ignored unless Visibility is 'Show'.
	[SiteCategoryId] INT NULL,

	-- Featured drives the default sort and the home page's selection; Badge is the ribbon on
	-- the product tile ("Best seller", "New"). Curated per store, never supplied by a feed.
	[Featured] BIT NOT NULL CONSTRAINT [DF_SiteProduct_Featured] DEFAULT 0,
	[Badge] NVARCHAR(40) NULL,

	[UpdatedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_SiteProduct_UpdatedUtc] DEFAULT SYSUTCDATETIME(),

	CONSTRAINT [PK_SiteProduct] PRIMARY KEY ([SiteId], [ProductId]),
	CONSTRAINT [FK_SiteProduct_ToSite] FOREIGN KEY ([SiteId]) REFERENCES [Site]([Id]),
	CONSTRAINT [FK_SiteProduct_ToProduct] FOREIGN KEY ([ProductId]) REFERENCES [Product]([Id]),
	-- Composite, as CategoryMapping's is: a store cannot file a product under another store's
	-- category. Not enforced while SiteCategoryId is NULL, which is the "no override" case.
	CONSTRAINT [FK_SiteProduct_ToSiteCategory] FOREIGN KEY ([SiteCategoryId], [SiteId])
		REFERENCES [SiteCategory]([Id], [SiteId]),
	CONSTRAINT [CK_SiteProduct_Visibility] CHECK ([Visibility] IS NULL OR [Visibility] IN (N'Show', N'Hide'))
)
