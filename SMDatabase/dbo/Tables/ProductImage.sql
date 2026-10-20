-- A product's photographs (T11), in the order its page shows them.
--
-- The bytes are in the product-images blob container, under StoredName, which the store
-- generated; the uploaded filename is not kept, because nothing shows it. The first image is
-- also written into Product.ProductImage as /img/{StoredName} by the procedures that change
-- this table, so every page that already renders that column shows it with no change.
CREATE TABLE [dbo].[ProductImage]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[ProductId] INT NOT NULL,
	[StoredName] NVARCHAR(64) NOT NULL,
	[ContentType] NVARCHAR(50) NOT NULL,
	[SortOrder] INT NOT NULL,
	[CreatedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_ProductImage_CreatedUtc] DEFAULT SYSUTCDATETIME(),

	CONSTRAINT [FK_ProductImage_ToProduct] FOREIGN KEY ([ProductId]) REFERENCES [Product]([Id]),
	CONSTRAINT [UQ_ProductImage_StoredName] UNIQUE ([StoredName])
)
GO

CREATE NONCLUSTERED INDEX [IX_ProductImage_Product]
	ON [dbo].[ProductImage] ([ProductId], [SortOrder])
	INCLUDE ([StoredName]);
