/*
The fiscal document each sale owes, from the moment it is owed until an issuer has issued it (T14):
Brazil's NF-e, and anything like it elsewhere.

dbo.EmailOutbox's shape, for its reasons. An issuer is a remote service that is sometimes down and
sometimes refuses, and a sale must be invoiced exactly once. So the row is written in the
transaction that makes the order owe one (spFiscalDocument_Enqueue), FiscalDocumentDispatcher in
StockApi claims it under a lease, and a failure is retried on OutboxRetryPolicy's schedule and
then dead-lettered, with an operator mail. Unlike a mail, a dead letter is not the end:
spFiscalDocument_Retry puts it back once somebody has fixed the cause.

One per order, by UQ_FiscalDocument_PurchaseId. Scoped by SiteId, which the enqueue takes from
the order rather than from a caller.
*/
CREATE TABLE [dbo].[FiscalDocument]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[SiteId] INT NOT NULL,
	[PurchaseId] INT NOT NULL,

	-- The store's Site.FiscalIssuer when the sale was made. Kept here so that switching a store
	-- to 'none' cannot silently drop a document it already owes.
	[Issuer] NVARCHAR(40) NOT NULL,

	[Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_FiscalDocument_Status] DEFAULT N'Pending',

	-- Counted at claim, as EmailOutbox.Attempts is, so an order that kills the host on every
	-- attempt still runs out of them.
	[Attempts] INT NOT NULL CONSTRAINT [DF_FiscalDocument_Attempts] DEFAULT 0,
	[NextAttemptUtc] DATETIME2 NOT NULL CONSTRAINT [DF_FiscalDocument_NextAttemptUtc] DEFAULT SYSUTCDATETIME(),
	[ClaimedUtc] DATETIME2 NULL,
	[ClaimToken] UNIQUEIDENTIFIER NULL,

	-- What the issuer answered: the document's number (an NF-e's access key, say) and, when it
	-- gives one, where the document can be read.
	[Number] NVARCHAR(100) NULL,
	[Url] NVARCHAR(400) NULL,

	[LastError] NVARCHAR(1000) NULL,
	[CreatedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_FiscalDocument_CreatedUtc] DEFAULT SYSUTCDATETIME(),
	[IssuedUtc] DATETIME2 NULL,

	CONSTRAINT [FK_FiscalDocument_ToSite] FOREIGN KEY ([SiteId]) REFERENCES [Site]([Id]),
	CONSTRAINT [FK_FiscalDocument_ToPurchase] FOREIGN KEY ([PurchaseId]) REFERENCES [Purchase]([Id]),
	CONSTRAINT [UQ_FiscalDocument_PurchaseId] UNIQUE ([PurchaseId]),
	-- As SQL Server stores it, not as IN (...). See CK_EmailOutbox_Status.
	CONSTRAINT [CK_FiscalDocument_Status] CHECK (
		[Status]=N'DeadLettered' OR [Status]=N'Issued' OR [Status]=N'Issuing' OR [Status]=N'Pending')
)
GO

-- The claim's whole search, as IX_EmailOutbox_Due is.
CREATE INDEX [IX_FiscalDocument_Due]
	ON [dbo].[FiscalDocument] ([Status], [NextAttemptUtc])
	INCLUDE ([ClaimedUtc]);
