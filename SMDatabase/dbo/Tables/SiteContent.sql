-- Editorial page content, per store. The storefront's Solutions, About, Contact and legal
-- pages differ between stores and must not be markup in shared components, so they live here
-- and the pages resolve through them.
--
-- Body is rendered as markup by the storefront, so rows are staff-authored only. Nothing that
-- reaches this table may come from customer input.
CREATE TABLE [dbo].[SiteContent]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[SiteId] INT NOT NULL,

	-- Matches the storefront route slug: 'about', 'solutions', 'contact', 'terms', 'privacy'.
	[ContentKey] NVARCHAR(80) NOT NULL,

	-- NULL is the fallback used when the site's locale has no row of its own, so a store can
	-- publish once and translate later without every page needing every language.
	[Locale] NVARCHAR(10) NULL,

	[Title] NVARCHAR(200) NOT NULL,
	[Lede] NVARCHAR(1000) NULL,
	[BodyHtml] NVARCHAR(MAX) NULL,

	[LastModified] DATETIME2 NOT NULL DEFAULT getutcdate(),

	CONSTRAINT [FK_SiteContent_ToSite] FOREIGN KEY ([SiteId]) REFERENCES [Site]([Id]),
	-- One row per key per locale per site. The unique index rather than a constraint so the
	-- nullable Locale still participates, which SQL Server treats as a single distinct value —
	-- exactly the "one fallback row" rule wanted here.
	CONSTRAINT [UQ_SiteContent_Key] UNIQUE ([SiteId], [ContentKey], [Locale])
)
