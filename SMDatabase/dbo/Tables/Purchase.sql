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

    /*
    Why this order was taxed the way it was, snapshotted rather than re-derived.

    The rate can change by statute, the rule set can be corrected, a customer can supply a
    VAT number they did not have at the time, and a store can move country. None of that may
    silently rewrite a document somebody has already acted on — the same argument T5 settled
    for price, one table over.

    TaxLegend is the sentence printed on the document. It is stored rather than rendered from
    the treatment because it is a legal statement, and improving the wording next year must
    not restate what last year's orders said.

    Both NULL on a POS row, like every other portal-only column here: a till sale has no
    account to assess.
    */
    [TaxTreatment] NVARCHAR(30) NULL,
    [TaxLegend] NVARCHAR(200) NULL,

    -- When payment is due: the order date plus Account.PaymentTermsDays, snapshotted because
    -- an account's terms can be renegotiated and an invoice already sent must not move.
    -- Equal to the order date for a prepaid account, which is what prepaid means.
    [DueDate] DATE NULL,

    /*
    Whether this order went past the account's credit limit.

    Recorded on every order and refused on only one path. A customer accepting their own quote
    is stopped; an admin converting one for somebody who rang up is making a commercial
    decision with their name on it, and the flag is how it stays visible afterwards. Same
    shape as T5's decidability rules, which gate the customer and not the procedure.
    */
    [CreditLimitExceeded] BIT NOT NULL DEFAULT 0,

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
    /*
    The four states a portal order moves through, and NULL for a POS sale.

    They had been a comment on the column since it was written, which is exactly where
    CK_Quote_Status was before T5 — and the same thing followed from it: spOrder_UpdateStatus
    stored whatever string arrived, so a typo produced an order that matched no filter and no
    step in the progress bar. NULL is admitted rather than tolerated: spPurchase_Insert sets
    no status at all, and a desktop sale genuinely has none.
    */
    CONSTRAINT [CK_Purchase_Status] CHECK (
        [Status] IS NULL
        OR [Status] IN (N'Awaiting payment', N'Processing', N'Fulfilled', N'Cancelled')),

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
