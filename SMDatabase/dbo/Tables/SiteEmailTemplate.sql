/*
A store's own wording for a message, replacing the platform's.

The SiteContent precedent: rows are staff-authored, and a store with no row gets the platform
default from EmailTemplates rather than another store's words. Either column may be NULL, which
keeps the platform's subject or body for that half.

Plain text with {Placeholder} tokens, and checked before it is used. EmailRenderer refuses a
row that names a placeholder the message does not have, or leaves out one it requires -- a
reset mail with no link, a rejection that does not say why -- and falls back to the platform
wording with a warning. Staff typing a template should not be able to send a customer a
message that cannot work.

Not markup, and must not become markup: customers' own words are substituted into several of
these messages, which is the SiteContent.BodyHtml rule running in reverse.

Since T9 the admin portal edits it (/admin/email), refusing on save what EmailRenderer would
refuse at send time.
*/
CREATE TABLE [dbo].[SiteEmailTemplate]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[SiteId] INT NOT NULL,
	-- An EmailTemplate key. A row for a key no template has is never read.
	[TemplateKey] NVARCHAR(80) NOT NULL,
	[Subject] NVARCHAR(200) NULL,
	[Body] NVARCHAR(MAX) NULL,
	[UpdatedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_SiteEmailTemplate_UpdatedUtc] DEFAULT SYSUTCDATETIME(),

	CONSTRAINT [FK_SiteEmailTemplate_ToSite] FOREIGN KEY ([SiteId]) REFERENCES [Site]([Id]),
	CONSTRAINT [UQ_SiteEmailTemplate_Key] UNIQUE ([SiteId], [TemplateKey])
)
