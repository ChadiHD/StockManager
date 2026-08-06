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
    CONSTRAINT [FK_Purchase_ToUser] FOREIGN KEY (StaffId) REFERENCES [User](UserId),
    CONSTRAINT [FK_Purchase_ToAccount] FOREIGN KEY ([AccountId]) REFERENCES [Account]([Id]),
    CONSTRAINT [FK_Purchase_ToQuote] FOREIGN KEY ([QuoteId]) REFERENCES [Quote]([Id])
)
