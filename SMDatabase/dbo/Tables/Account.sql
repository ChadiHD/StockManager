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

    /*
    Company identifiers collected at registration. Which of them a given store demands is its
    registration field set's decision, so both are nullable here — an EU B2B application needs
    a VAT number and an export one does not, and the schema is the wrong place to take sides.

    VatNumber is what the reverse-charge treatment in T6 will be decided from, which is why it
    is a column rather than a document: a scanned certificate cannot be compared to a country
    code.
    */
    [VatNumber] NVARCHAR(30) NULL,
    [RegistrationNumber] NVARCHAR(50) NULL,

    /*
    Who approved or rejected this application, when, and why.

    The approvals workflow used to change Status and record nothing else. That is fine while
    one person runs one store and remembers; it stops being fine the moment a customer asks
    why they were turned down, or a second admin wants to know whether an account was vetted
    or waved through. RejectionReason is also what the rejection email has to quote.
    */
    [ApprovedUtc] DATETIME2 NULL,
    [ApprovedBy] NVARCHAR(128) NULL,
    [RejectionReason] NVARCHAR(500) NULL,

    [CreatedDate] DATETIME2 NOT NULL DEFAULT getutcdate(),

    -- The store this account belongs to. NOT NULL since every write path sets it: the insert
    -- procedures resolve it through dbo.fnSite_Resolve and refuse rather than guess. A
    -- nullable scope column is not merely untidy — a composite foreign key is not enforced
    -- when any of its columns is NULL, so FK_Quote_ToAccount and FK_Purchase_ToAccount only
    -- bite while this has a value.
    [SiteId] INT NOT NULL,
    CONSTRAINT [UQ_Account_Reference] UNIQUE ([Reference]),
    -- Lets Quote and Purchase reference an account together with its site.
    CONSTRAINT [UQ_Account_IdSite] UNIQUE ([Id], [SiteId]),
    -- Composite: an account may only sit in a group belonging to the same store. Keyed on
    -- CustomerGroupId alone this permits one store's account to take another store's discount
    -- rate. A composite foreign key is not checked when any of its columns is NULL, so an
    -- account with no group is unaffected.
    CONSTRAINT [FK_Account_ToCustomerGroup] FOREIGN KEY ([CustomerGroupId], [SiteId])
        REFERENCES [CustomerGroup]([Id], [SiteId]),
    CONSTRAINT [FK_Account_ToSite] FOREIGN KEY ([SiteId]) REFERENCES [Site]([Id]),
    CONSTRAINT [FK_Account_ApprovedBy] FOREIGN KEY ([ApprovedBy]) REFERENCES [User]([UserId])
)
