CREATE TABLE [dbo].[Product]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
    [ProductName] NVARCHAR(100) NOT NULL,
    [Description] NVARCHAR(MAX) NOT NULL,
    [RetailPrice] MONEY  NOT NULL,
    [QuantityInStock] INT NOT NULL DEFAULT 1,
    [CreatedDate] DATETIME2 NOT NULL DEFAULT getutcdate(),
    [LastModified] DATETIME2 NOT NULL DEFAULT getutcdate(),
    [IsTaxable] BIT NOT NULL DEFAULT 1,
    [ProductImage] NVARCHAR(500) NULL,
    -- Catalog attributes used by /admin/products. Nullable so existing rows and the
    -- desktop POS (which never sets them) are unaffected.
    [Sku] NVARCHAR(50) NULL,
    [Category] NVARCHAR(50) NULL,
    [Cost] MONEY NULL,
    -- 'Own' | 'Distributor'
    [Source] NVARCHAR(20) NULL,
    [Distributor] NVARCHAR(100) NULL,
    [DistributorSku] NVARCHAR(50) NULL,
    [LastSynced] DATETIME2 NULL,
    -- Set when a distributor product stops appearing in its feed. Kept rather than deleted so
    -- historical quote and order lines still resolve to a product.
    [Delisted] BIT NOT NULL DEFAULT 0,
    -- Identity keys used to look product content up with Icecat. Brand + part number is present
    -- on every row of the FlexIT feed; EAN only on a minority, so it is the fallback.
    [Manufacturer] NVARCHAR(100) NULL,
    [ManufacturerPartNumber] NVARCHAR(100) NULL,
    [Ean] NVARCHAR(20) NULL,
    -- The feed's "IceCatID" column is a Yes/empty availability flag, not an identifier, so it
    -- is stored as a hint for which products to try first rather than as a lookup key.
    [IcecatAvailable] BIT NULL,
    -- When an image was last resolved, so enrichment can skip what it already has and retry
    -- what it could not find.
    [ImageSourcedUtc] DATETIME2 NULL,
    [ImageLookupUtc] DATETIME2 NULL,

    /*
    Where a product came from and what its prices are in (T9). Until then neither was recorded:
    Distributor held the feed's display name, which was also the merge key, and RetailPrice and
    Cost were bare numbers. With a GBP feed beside an EUR one, a UK store mapping "Notebooks"
    would have sold the Irish distributor's notebooks with a pound sign on euro prices.

    FeedId is the feed that imported the row: the merge and delist key, so renaming a feed keeps
    its products, and the store that may sell them, which is the feed's own. NULL for own stock
    and for orphans — a distributor product whose feed is gone, which no store sells.
    CurrencyCode is the currency of RetailPrice and Cost: the feed's store's for imported rows,
    the acting store's for a product an admin adds. dbo.fnSite_ProductSellable reads both.

    The EUR default is for rows nothing sets it on — the desktop till's, and test fixtures —
    the same default Site, Account and Quote carry. Every production write path sets it. Last
    in the table, so the publish appends rather than rebuilds.
    */
    [FeedId] INT NULL,
    [CurrencyCode] NVARCHAR(3) NOT NULL CONSTRAINT [DF_Product_CurrencyCode] DEFAULT 'EUR',

    -- SET NULL: deleting a feed stops its stock selling anywhere, and quotes and orders that
    -- hold its products still resolve them.
    CONSTRAINT [FK_Product_ToDistributorFeed] FOREIGN KEY ([FeedId])
        REFERENCES [DistributorFeed]([Id]) ON DELETE SET NULL

    /*
    No Published, Featured or Badge. They were here until T8, and they were store decisions on a
    table every store shares: unpublishing a product for one store took it off all of them.
    They live on dbo.SiteProduct now, per store. Delisted stays, because it is the distributor's
    fact about the product rather than any store's choice.
    */
)
GO

-- The catalog query filters on these before anything else, and pages over the result, so the
-- gate column leads and Category follows for the mapping lookup.
CREATE NONCLUSTERED INDEX [IX_Product_CatalogGate]
	ON [dbo].[Product] ([Delisted], [Category])
	-- ManufacturerPartNumber is here for a reason the other columns are not. The relevance
	-- ladder tests MPN before any prefix match, and that ladder runs for every row the query
	-- has to *rank*, not just the page it returns — leaving it out costs a key lookup per
	-- visible row on every search. Featured, the default sort key, left with the column: it is
	-- on dbo.SiteProduct now, reached through the placement join.
	INCLUDE ([Sku], [ProductName], [ManufacturerPartNumber], [RetailPrice], [QuantityInStock],
	         [Distributor], [Manufacturer],
	         -- dbo.fnSite_ProductSellable's inputs, which every catalog query now asks first (T9).
	         [FeedId], [CurrencyCode]);
GO

-- The feed sync merges and delists on these (T9); before, on the feed's name and DistributorSku,
-- which nothing indexed.
CREATE NONCLUSTERED INDEX [IX_Product_Feed]
	ON [dbo].[Product] ([FeedId], [DistributorSku])
	WHERE [FeedId] IS NOT NULL;
