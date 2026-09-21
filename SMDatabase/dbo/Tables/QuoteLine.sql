CREATE TABLE [dbo].[QuoteLine]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
    [QuoteId] INT NOT NULL,
    [ProductId] INT NOT NULL,
    [Quantity] INT NOT NULL DEFAULT 1,
    [ListPrice] MONEY NOT NULL,

    -- The discount that produced NetPrice, for display and for the quote document.
    --
    -- DECIMAL rather than INT from T5. A group's own discount is an integer
    -- (CustomerGroup.Discount), but the site's margin floor is not — Site.MinMarginPct is
    -- DECIMAL(5, 2) — and a floored price's effective discount is whatever the floor makes it.
    -- Recorded as an integer, a floored line would show a discount that does not produce the
    -- price beside it.
    [DiscountPct] DECIMAL(5, 2) NOT NULL DEFAULT 0,

    -- What the customer is charged per unit. Stated by the caller on the storefront path and
    -- derived by spQuoteLine_Insert when it is not — see that procedure for why deriving it
    -- unconditionally was wrong the moment a price came from PriceResolver.
    [NetPrice] MONEY NOT NULL,

    CONSTRAINT [FK_QuoteLine_ToQuote] FOREIGN KEY ([QuoteId]) REFERENCES [Quote]([Id]),
    CONSTRAINT [FK_QuoteLine_ToProduct] FOREIGN KEY ([ProductId]) REFERENCES [Product]([Id])
)
