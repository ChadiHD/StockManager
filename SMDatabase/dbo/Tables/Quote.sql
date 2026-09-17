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

    -- Anything the customer wrote alongside their request: a delivery deadline, a project
    -- reference, a question about compatibility. Kept because sales reads it before pricing,
    -- and a quote priced without it gets re-quoted.
    --
    -- Rendered as text, never as markup. This is the one column on this table whose contents
    -- originate with a customer, so the SiteContent.BodyHtml rule applies in reverse: nothing
    -- here may reach a MarkupString.
    [CustomerNote] NVARCHAR(1000) NULL,
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
    CONSTRAINT [FK_Quote_ToSite] FOREIGN KEY ([SiteId]) REFERENCES [Site]([Id]),
    -- Documented in a comment since this table was written, enforced from T5 because the
    -- accept path now gates on it: spOrder_ConvertFromQuote converts a quote only while it
    -- still reads 'Priced', and a status nobody spells the same way twice makes that guard a
    -- no-op that looks like a guard.
    CONSTRAINT [CK_Quote_Status] CHECK ([Status] IN (N'Requested', N'Priced', N'Accepted', N'Rejected'))
)
