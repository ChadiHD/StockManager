-- Files a customer uploads to support a trading application: a VAT certificate, a Chamber of
-- Commerce extract, whatever the site's registration field set demands.
--
-- Metadata only. The bytes live in an IDocumentStore — the local filesystem in development,
-- blob storage in production — outside the web root either way, reachable only through an
-- endpoint that re-checks who is asking. Nothing here is ever turned into a URL.
CREATE TABLE [dbo].[AccountDocument]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[AccountId] INT NOT NULL,

	-- 'VatCertificate' | 'ChamberOfCommerce' | 'Other'. Which of these a registration must
	-- supply is the field set's decision, not this table's.
	[Kind] NVARCHAR(40) NOT NULL,

	/*
	The name the bytes are stored under, generated here and never supplied by the uploader.

	An uploaded filename is attacker-controlled text. Used as a path it carries traversal and
	overwrite; used in a header it carries injection; and on a case-insensitive filesystem two
	customers' "certificate.pdf" are one file. Storing under a generated id removes all of
	that at once, and OriginalName below keeps the human-readable name for display only.
	*/
	[StoredName] NVARCHAR(100) NOT NULL,

	-- What the customer called it. Display only, and must be encoded on the way out — it is
	-- as untrusted as it was on the way in.
	[OriginalName] NVARCHAR(260) NOT NULL,

	-- Recorded as sniffed from the file's leading bytes, not as declared by the client, and
	-- checked against an allow-list before any of this row is written.
	[ContentType] NVARCHAR(100) NOT NULL,

	[SizeBytes] BIGINT NOT NULL,

	-- Who uploaded it. Nullable because an admin may attach a document on a customer's behalf
	-- and staff are not contacts.
	[UploadedByContactId] INT NULL,

	[UploadedUtc] DATETIME2 NOT NULL DEFAULT getutcdate(),

	-- 'Pending' | 'Accepted' | 'Rejected'. Reviewed as part of approving the account, so a
	-- document that fails review does not have to fail the whole application.
	[Status] NVARCHAR(20) NOT NULL DEFAULT 'Pending',

	CONSTRAINT [FK_AccountDocument_ToAccount] FOREIGN KEY ([AccountId]) REFERENCES [Account]([Id]),
	CONSTRAINT [FK_AccountDocument_ToContact] FOREIGN KEY ([UploadedByContactId]) REFERENCES [Contact]([Id]),

	-- The stored name is what the document store is keyed by, so a collision would serve one
	-- customer another's file.
	CONSTRAINT [UQ_AccountDocument_StoredName] UNIQUE ([StoredName]),

	CONSTRAINT [CK_AccountDocument_Kind] CHECK ([Kind] IN ('VatCertificate', 'ChamberOfCommerce', 'Other')),
	CONSTRAINT [CK_AccountDocument_Status] CHECK ([Status] IN ('Pending', 'Accepted', 'Rejected')),

	-- Belt and braces against the API's own cap. A row claiming a size it cannot have means
	-- the upload path was bypassed.
	CONSTRAINT [CK_AccountDocument_SizeBytes] CHECK ([SizeBytes] > 0 AND [SizeBytes] <= 10485760)
)
GO

-- The admin's review screen lists an account's documents; the customer's account area lists
-- their own.
CREATE NONCLUSTERED INDEX [IX_AccountDocument_Account]
	ON [dbo].[AccountDocument] ([AccountId])
	INCLUDE ([Kind], [Status], [OriginalName], [UploadedUtc]);
