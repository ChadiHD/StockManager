/*
Every message the platform owes somebody, from the moment it is owed until it has gone.

Before T6 a send happened after the write had committed, in the request that made it, and a
process that died between the two lost the message with nothing recording it was owed. A row
here is written in the same transaction as the change it reports wherever a procedure makes
that change -- spAccount_Approve, spAccount_Reject -- so the decision and the intent to tell
somebody about it commit together or not at all. EmailDispatcher in StockApi does the sending.

The payload is values, not rendered text. A template fixed after a row was written applies to
a message that has not gone yet; a rendered body would freeze the bug in, along with whatever
the site's name was on the day the row was written.

Scoped by SiteId like everything else a customer can be sent: the from-address, the signature
and the sender reputation are a store's own, and the renderer reads the store's copy.
*/
CREATE TABLE [dbo].[EmailOutbox]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[SiteId] INT NOT NULL,
	[ToAddress] NVARCHAR(256) NOT NULL,
	[ToName] NVARCHAR(200) NULL,

	-- Names an EmailTemplate in SMDataManager.Library/Email. A key the registry does not know
	-- is retried like any other failure and dead-letters, rather than being dropped: it means
	-- a host older than the row is dispatching, and a newer one may yet arrive.
	[TemplateKey] NVARCHAR(80) NOT NULL,

	/*
	The template's values as JSON, or the Data Protection ciphertext of that JSON when
	PayloadProtected is set.

	Protected whenever the template carries a credential. A password-reset or
	email-confirmation link is a way into an account, and a row in this table is backed up,
	restored into development and read by anyone with SELECT on SMDatabase -- the same
	argument that stops LoggingEmailSender logging bodies outside Development. The key ring
	lives in ApiAuthDb, so a copy of this database alone does not open it.

	Nulled once a protected message has been sent or given up on: the credential has done its
	job, or the customer will ask for another, and holding it longer is holding it for nobody.
	Rows written by a procedure are never protected, because nothing a procedure writes here
	is not already sitting in plain text in the table it came from.
	*/
	[PayloadJson] NVARCHAR(MAX) NULL,
	[PayloadProtected] BIT NOT NULL CONSTRAINT [DF_EmailOutbox_PayloadProtected] DEFAULT 0,

	[Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_EmailOutbox_Status] DEFAULT N'Pending',

	-- Incremented when a row is claimed, not when a send fails, so a message that takes the
	-- host down with it on every attempt still runs out of attempts rather than being
	-- reclaimed for ever once each lease expires.
	[Attempts] INT NOT NULL CONSTRAINT [DF_EmailOutbox_Attempts] DEFAULT 0,
	[NextAttemptUtc] DATETIME2 NOT NULL CONSTRAINT [DF_EmailOutbox_NextAttemptUtc] DEFAULT SYSUTCDATETIME(),

	-- The lease. A dispatcher killed mid-send leaves a row in Sending, and without an expiry
	-- that message would never go and nothing would say why. See spEmailOutbox_Claim.
	[ClaimedUtc] DATETIME2 NULL,
	-- Which claim holds the row, so a dispatcher whose lease ran out and was overtaken cannot
	-- record its late answer over the newer claimant's.
	[ClaimToken] UNIQUEIDENTIFIER NULL,

	[LastError] NVARCHAR(1000) NULL,
	[CreatedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_EmailOutbox_CreatedUtc] DEFAULT SYSUTCDATETIME(),
	[SentUtc] DATETIME2 NULL,

	CONSTRAINT [FK_EmailOutbox_ToSite] FOREIGN KEY ([SiteId]) REFERENCES [Site]([Id]),
	CONSTRAINT [CK_EmailOutbox_Status] CHECK (
		[Status] IN (N'Pending', N'Sending', N'Sent', N'DeadLettered'))
)
GO

-- The claim's whole search. Sent and dead-lettered rows are the bulk of the table and are
-- never claimed, so the leading Status column keeps the claim reading only the live ones.
CREATE INDEX [IX_EmailOutbox_Due]
	ON [dbo].[EmailOutbox] ([Status], [NextAttemptUtc])
	INCLUDE ([ClaimedUtc]);
