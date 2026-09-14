-- A trading account: a customer COMPANY. Distinct from dbo.[User], which holds staff
-- profiles for the people who sign in (dbo.Purchase.StaffId references that table).
CREATE TABLE [dbo].[Account]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
    [Reference] NVARCHAR(20) NOT NULL,
    [Company] NVARCHAR(200) NOT NULL,
    [ContactName] NVARCHAR(100) NULL,
    [Email] NVARCHAR(256) NULL,
    [Country] NVARCHAR(100) NULL,
    [Currency] NVARCHAR(3) NOT NULL DEFAULT 'EUR',
    [CustomerGroupId] INT NULL,
    [PaymentMethod] NVARCHAR(50) NOT NULL DEFAULT 'Card',
    [PaymentTerms] NVARCHAR(50) NOT NULL DEFAULT 'Prepaid',
    [CreditLimit] MONEY NOT NULL DEFAULT 0,
    -- Drives the approvals workflow on /admin/accounts: Pending | Approved | Rejected | Suspended
    [Status] NVARCHAR(20) NOT NULL DEFAULT 'Pending',
    [CreatedDate] DATETIME2 NOT NULL DEFAULT getutcdate(),
    -- The store this account belongs to. Nullable only because rows created before multi-site
    -- have no answer; Scripts/PostDeployment/Seed.sql backfills them to the default site.
    -- Tighten to NOT NULL once every write path sets it.
    [SiteId] INT NULL,
    CONSTRAINT [UQ_Account_Reference] UNIQUE ([Reference]),
    -- Lets Quote and Purchase reference an account together with its site.
    CONSTRAINT [UQ_Account_IdSite] UNIQUE ([Id], [SiteId]),
    -- Composite: an account may only sit in a group belonging to the same store. Keyed on
    -- CustomerGroupId alone this permits one store's account to take another store's discount
    -- rate. A composite foreign key is not checked when any of its columns is NULL, so an
    -- account with no group is unaffected.
    CONSTRAINT [FK_Account_ToCustomerGroup] FOREIGN KEY ([CustomerGroupId], [SiteId])
        REFERENCES [CustomerGroup]([Id], [SiteId]),
    CONSTRAINT [FK_Account_ToSite] FOREIGN KEY ([SiteId]) REFERENCES [Site]([Id])
)
