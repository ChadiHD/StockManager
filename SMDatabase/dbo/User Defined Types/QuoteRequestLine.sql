-- Carries a whole basket to spQuote_SubmitRequest in one round trip.
--
-- A basket is small, so this is not about volume like dbo.DistributorFeedItem. It is about the
-- transaction: the quote, its lines and the emptying of the basket have to be one atomic write,
-- and a per-line round trip inside a transaction holds it open across the network for as many
-- turns as the customer has products.
--
-- Prices are stated rather than derived. They come from PriceResolver via CatalogPresenter, so
-- the quote records the price the customer was looking at when they pressed submit -- see
-- spQuoteLine_Insert for why a re-derived net price is a different number.
CREATE TYPE [dbo].[QuoteRequestLine] AS TABLE
(
	[ProductId] INT NOT NULL,
	[Quantity] INT NOT NULL,
	[ListPrice] MONEY NOT NULL,
	[DiscountPct] DECIMAL(5, 2) NOT NULL,
	[NetPrice] MONEY NOT NULL,
	PRIMARY KEY ([ProductId])
)
