CREATE TABLE [dbo].[QuoteLine]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
    [QuoteId] INT NOT NULL,
    [ProductId] INT NOT NULL,
    [Quantity] INT NOT NULL DEFAULT 1,
    [ListPrice] MONEY NOT NULL,
    [DiscountPct] INT NOT NULL DEFAULT 0,
    [NetPrice] MONEY NOT NULL,
    CONSTRAINT [FK_QuoteLine_ToQuote] FOREIGN KEY ([QuoteId]) REFERENCES [Quote]([Id]),
    CONSTRAINT [FK_QuoteLine_ToProduct] FOREIGN KEY ([ProductId]) REFERENCES [Product]([Id])
)
