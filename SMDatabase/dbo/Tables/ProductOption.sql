-- What makes one variant of a style different from another (T11): Size = M, Colour = Navy.
--
-- The name is the store's own word, in its own language, so nothing in the platform names Size
-- or Colour. One value per name per variant; spProduct_AddVariant refuses a variant whose
-- options repeat a sibling's, because two rows a customer cannot tell apart are one product
-- with two stock counts.
CREATE TABLE [dbo].[ProductOption]
(
	[ProductId] INT NOT NULL,
	[Name] NVARCHAR(50) NOT NULL,
	[Value] NVARCHAR(50) NOT NULL,

	-- Where the option sits in the variant's label and in the facet rail: Size before Colour.
	[SortOrder] INT NOT NULL CONSTRAINT [DF_ProductOption_SortOrder] DEFAULT 0,

	CONSTRAINT [PK_ProductOption] PRIMARY KEY ([ProductId], [Name]),
	CONSTRAINT [FK_ProductOption_ToProduct] FOREIGN KEY ([ProductId]) REFERENCES [Product]([Id])
)
GO

-- The facet rail and the option filter read by (name, value) across a store's variants.
CREATE NONCLUSTERED INDEX [IX_ProductOption_NameValue]
	ON [dbo].[ProductOption] ([Name], [Value]);
