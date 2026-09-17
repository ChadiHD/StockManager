CREATE TABLE [dbo].[Quote]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
    [Reference] NVARCHAR(20) NOT NULL,
    [AccountId] INT NOT NULL,
    [Currency] NVARCHAR(3) NOT NULL DEFAULT 'EUR',
    -- Requested | Priced | Accepted | Rejected
    [Status] NVARCHAR(20) NOT NULL DEFAULT 'Requested',
    [CreatedDate] DATETIME2 NOT NULL DEFAULT getutcdate(),
    [ExpiresDate] DATETIME2 NULL,
    -- Denormalised from Account so quote queries can scope by site without a join. NOT NULL,
    -- because a composite foreign key stops being enforced the moment one of its columns is
    -- NULL — FK_Quote_ToAccount below would become advisory.
    [SiteId] INT NOT NULL,
    CONSTRAINT [UQ_Quote_Reference] UNIQUE ([Reference]),
    -- Lets Purchase reference a quote together with its site.
    CONSTRAINT [UQ_Quote_IdSite] UNIQUE ([Id], [SiteId]),
    -- Composite: a quote belongs to the store its account belongs to, enforced rather than
    -- assumed. spQuote_Insert already derives SiteId from the account; this is what stops a
    -- future caller doing otherwise.
    CONSTRAINT [FK_Quote_ToAccount] FOREIGN KEY ([AccountId], [SiteId])
        REFERENCES [Account]([Id], [SiteId]),
    CONSTRAINT [FK_Quote_ToSite] FOREIGN KEY ([SiteId]) REFERENCES [Site]([Id])
)
