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
    -- Denormalised from Account so quote queries can scope by site without a join. Nullable
    -- for rows predating multi-site; backfilled by Scripts/PostDeployment/Seed.sql.
    [SiteId] INT NULL,
    CONSTRAINT [UQ_Quote_Reference] UNIQUE ([Reference]),
    CONSTRAINT [FK_Quote_ToAccount] FOREIGN KEY ([AccountId]) REFERENCES [Account]([Id]),
    CONSTRAINT [FK_Quote_ToSite] FOREIGN KEY ([SiteId]) REFERENCES [Site]([Id])
)
