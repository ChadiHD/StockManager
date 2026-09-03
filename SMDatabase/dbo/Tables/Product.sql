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
    [ImageLookupUtc] DATETIME2 NULL
)
