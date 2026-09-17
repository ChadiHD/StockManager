CREATE TABLE [dbo].[Purchase]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
    -- The member of staff who raised this. NULLable since T5, because a portal order can be
    -- placed by a customer accepting their own quote, and dbo.[User] holds staff only —
    -- see PlacedByContactId and CK_Purchase_Placer below.
    [StaffId] NVARCHAR(128) NULL,
    [PurchaseDate] DATETIME2 NOT NULL ,
    [SubTotal] MONEY NOT NULL,
    [VAT] MONEY NOT NULL,
    [FinalPrice] MONEY NOT NULL,
    -- Sales-order attributes for /admin/orders. Every column below is NULLable: the WPF
    -- desktop POS inserts through spPurchase_Insert without them, so its rows stay valid
    -- and represent a plain completed sale (no Reference / Status / Account).
    [Reference] NVARCHAR(20) NULL,
    [AccountId] INT NULL,
    [QuoteId] INT NULL,
    [Currency] NVARCHAR(3) NULL,
    -- Awaiting payment | Processing | Fulfilled | Cancelled
    [Status] NVARCHAR(30) NULL,
    -- Set on portal orders only. Desktop POS sales have no site and keep it NULL — unlike the
    -- other scoped tables, this column is not backfilled, because a POS sale genuinely does not
    -- belong to a storefront.
    [SiteId] INT NULL,

    -- The customer's employee who accepted the quote this order came from. Customers are
    -- Contacts and staff are dbo.[User] rows, and the two are different tables on purpose, so
    -- an order needs a column for each rather than one column holding whichever applies.
    [PlacedByContactId] INT NULL,

    -- The customer's own purchase-order number, captured at acceptance and quoted back on
    -- every document. Theirs, not ours: many B2B buyers cannot pay an invoice that does not
    -- carry it, which is why it is on the order and not a note somewhere.
    [PoNumber] NVARCHAR(50) NULL,

    CONSTRAINT [FK_Purchase_ToUser] FOREIGN KEY (StaffId) REFERENCES [User](UserId),
    -- Composite, like Quote's. A POS sale leaves AccountId, QuoteId and SiteId all NULL, and
    -- a composite foreign key is not checked when any column is NULL, so the desktop path is
    -- untouched while a portal order is held to one store throughout.
    CONSTRAINT [FK_Purchase_ToAccount] FOREIGN KEY ([AccountId], [SiteId])
        REFERENCES [Account]([Id], [SiteId]),
    CONSTRAINT [FK_Purchase_ToQuote] FOREIGN KEY ([QuoteId], [SiteId])
        REFERENCES [Quote]([Id], [SiteId]),
    CONSTRAINT [FK_Purchase_ToSite] FOREIGN KEY ([SiteId]) REFERENCES [Site]([Id]),

    -- Composite over the account for the same reason FK_Purchase_ToAccount is: a Contact
    -- belongs to one company, and an order placed by another company's buyer is a mismatch
    -- the database can refuse rather than a check every caller has to remember. Unchecked
    -- while AccountId is NULL, so POS rows are unaffected.
    CONSTRAINT [FK_Purchase_ToContact] FOREIGN KEY ([PlacedByContactId], [AccountId])
        REFERENCES [Contact]([Id], [AccountId]),

    -- Exactly one placer, always. Staff for a POS sale or an admin conversion, a contact for a
    -- customer acceptance, and never both — an order attributed to two people answers the
    -- question "who placed this?" with a guess. Making StaffId nullable removed the only thing
    -- that was stopping a row with no placer at all, so this replaces it.
    CONSTRAINT [CK_Purchase_Placer] CHECK (
        CASE WHEN [StaffId] IS NULL THEN 0 ELSE 1 END
      + CASE WHEN [PlacedByContactId] IS NULL THEN 0 ELSE 1 END = 1)
)
GO

-- One order per quote. spOrder_ConvertFromQuote claims the quote's status transition so two
-- callers cannot both convert it, and this is the backstop under that claim: a second order
-- against the same quote is refused by the database even if some future path skips the
-- procedure. Filtered because POS rows leave QuoteId NULL and SQL Server treats NULL as one
-- distinct value in a unique index — unfiltered, the second POS sale ever would fail.
CREATE UNIQUE NONCLUSTERED INDEX [UQ_Purchase_QuoteId]
	ON [dbo].[Purchase] ([QuoteId])
	WHERE [QuoteId] IS NOT NULL;
