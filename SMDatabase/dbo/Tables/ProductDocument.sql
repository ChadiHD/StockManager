-- A product's documents — datasheets, manuals, certificates — in the order they were added.
--
-- The bytes are in the product-documents blob container under StoredName, which the store
-- generated. FileName is the uploader's, kept to label the link and name the download; it never
-- names anything in storage. Shared by every store, as dbo.Product and dbo.ProductImage are.
CREATE TABLE [dbo].[ProductDocument]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[ProductId] INT NOT NULL,
	[StoredName] NVARCHAR(64) NOT NULL,
	[FileName] NVARCHAR(120) NOT NULL,
	[ContentType] NVARCHAR(100) NOT NULL,
	[SizeBytes] BIGINT NOT NULL,
	[CreatedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_ProductDocument_CreatedUtc] DEFAULT SYSUTCDATETIME(),

	CONSTRAINT [FK_ProductDocument_ToProduct] FOREIGN KEY ([ProductId]) REFERENCES [Product]([Id]),
	CONSTRAINT [UQ_ProductDocument_StoredName] UNIQUE ([StoredName])
)
GO

CREATE NONCLUSTERED INDEX [IX_ProductDocument_Product]
	ON [dbo].[ProductDocument] ([ProductId], [Id]);
