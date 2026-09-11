-- Translates a distributor's brand string into the name a content provider knows.
--
-- Distributors ship their own vocabulary: the FlexIT feed calls Hewlett-Packard "HPINC", which
-- matches nothing at Icecat, and files accessories under "UNIVERSAL", which is not a brand at
-- all. Icecat looks products up by brand plus part number, so an untranslated brand string
-- fails every lookup for that manufacturer — 69% of the catalog in FlexIT's case — and looks
-- exactly like "the provider has no content for us".
CREATE TABLE [dbo].[BrandAlias]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,

	-- dbo.Product.Distributor, or NULL for an alias that holds whoever supplied the row.
	-- Most are universal; a distributor-specific row wins over the universal one, the same
	-- precedence dbo.SiteContent uses for locale.
	[Distributor] NVARCHAR(100) NULL,

	-- The value as it arrives in the feed, matched against dbo.Product.Manufacturer.
	[FeedBrand] NVARCHAR(100) NOT NULL,

	-- The provider's name for the same manufacturer. NULL means the feed value is not a brand
	-- at all ("UNIVERSAL", "NOBRAND"), so brand lookups are skipped for it and only the EAN
	-- fallback is attempted — cheaper than spending a request to be told no.
	[IcecatBrand] NVARCHAR(100) NULL,

	[Note] NVARCHAR(300) NULL,
	[CreatedDate] DATETIME2 NOT NULL DEFAULT getutcdate(),

	CONSTRAINT [UQ_BrandAlias_FeedBrand] UNIQUE ([Distributor], [FeedBrand])
)
