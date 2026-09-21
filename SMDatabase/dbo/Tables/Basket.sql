/*
A basket in progress: the list a customer is building before it becomes a quote request.

Not a dbo.Quote with a 'Draft' status, and the reasons are structural rather than tidiness:

  - Quote.AccountId is NOT NULL with a composite foreign key to Account(Id, SiteId), so an
    anonymous visitor's basket cannot be a Quote row at all.
  - Quote.Reference comes from dbo.QuoteReferenceSequence on insert, so every abandoned basket
    would burn a QT- number and the references customers see would have gaps proportional to
    the store's bounce rate.
  - spQuote_GetAll feeds /admin/quotes and spActivity_GetRecent feeds the dashboard. A draft
    would appear in both as a request nobody made, and suppressing it means a
    "Status <> 'Draft'" predicate in every query over that table, for ever — which is
    dbo.Purchase's double duty all over again.

Found by its ContactId when somebody is signed in and by its Token when nobody is. The token
lives in an HttpOnly cookie and is the authorisation to read this basket, so BasketService
mints it from a cryptographic RNG: an id of 41 would let anyone read basket 42.
*/
CREATE TABLE [dbo].[Basket]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[SiteId] INT NOT NULL,

	-- NULL until somebody signs in. spBasket_Claim attaches it, and checks that the contact
	-- belongs to this store first — Contact carries no SiteId of its own, only its account's.
	[ContactId] INT NULL,

	-- Base64url of 32 random bytes as BasketService mints it. NVARCHAR rather than a
	-- UNIQUEIDENTIFIER because a GUID is 122 bits of which some are structure, and this value
	-- is a bearer credential.
	[Token] NVARCHAR(64) NOT NULL,

	[CreatedUtc] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),

	-- Moved by every line change. Nothing reads it yet; the sweep that will is T7's, with the
	-- hosting decision. Kept rather than added later because every write here already sets it.
	[UpdatedUtc] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),

	CONSTRAINT [UQ_Basket_Token] UNIQUE ([Token]),
	CONSTRAINT [FK_Basket_ToSite] FOREIGN KEY ([SiteId]) REFERENCES [Site]([Id]),
	CONSTRAINT [FK_Basket_ToContact] FOREIGN KEY ([ContactId]) REFERENCES [Contact]([Id])
)
GO

-- One basket per signed-in contact. Filtered, because a unique constraint over a nullable
-- column would allow exactly one anonymous basket in the whole database.
--
-- Per contact rather than per account: two buyers at one company each building their own list
-- is the expected thing, and one shared list they can both edit would be a surprise.
CREATE UNIQUE NONCLUSTERED INDEX [UQ_Basket_Contact]
	ON [dbo].[Basket] ([ContactId])
	WHERE [ContactId] IS NOT NULL;
