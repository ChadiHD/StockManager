-- Carries an entire distributor feed to the database in one round trip. Feeds run to a few
-- thousand rows (the FlexIT feed is ~2,300), which is far too many to send one call at a time.
CREATE TYPE [dbo].[DistributorFeedItem] AS TABLE
(
	[DistributorSku] NVARCHAR(50) NOT NULL,
	[Sku] NVARCHAR(50) NULL,
	[ProductName] NVARCHAR(100) NOT NULL,
	[Description] NVARCHAR(MAX) NULL,
	[Category] NVARCHAR(50) NULL,
	[Cost] MONEY NULL,
	[Srp] MONEY NULL,
	[QuantityInStock] INT NOT NULL,
	[Manufacturer] NVARCHAR(100) NULL,
	[ManufacturerPartNumber] NVARCHAR(100) NULL,
	[Ean] NVARCHAR(20) NULL,
	[IcecatAvailable] BIT NULL,
	PRIMARY KEY ([DistributorSku])
)
