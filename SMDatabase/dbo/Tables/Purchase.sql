CREATE TABLE [dbo].[Purchase]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
    [StaffId] NVARCHAR(128) NOT NULL,
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
    CONSTRAINT [FK_Purchase_ToUser] FOREIGN KEY (StaffId) REFERENCES [User](UserId),
    -- Composite, like Quote's. A POS sale leaves AccountId, QuoteId and SiteId all NULL, and
    -- a composite foreign key is not checked when any column is NULL, so the desktop path is
    -- untouched while a portal order is held to one store throughout.
    CONSTRAINT [FK_Purchase_ToAccount] FOREIGN KEY ([AccountId], [SiteId])
        REFERENCES [Account]([Id], [SiteId]),
    CONSTRAINT [FK_Purchase_ToQuote] FOREIGN KEY ([QuoteId], [SiteId])
        REFERENCES [Quote]([Id], [SiteId]),
    CONSTRAINT [FK_Purchase_ToSite] FOREIGN KEY ([SiteId]) REFERENCES [Site]([Id])
)
