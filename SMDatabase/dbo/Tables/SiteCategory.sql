-- A store's own catalog taxonomy. Distributor feeds carry their own category strings
-- ("Keyboards / Desktops", "Tablet PC's"), which are the distributor's filing system rather
-- than anything a buyer would navigate, so each store defines the categories it wants and
-- dbo.CategoryMapping decides which feed values land in each.
CREATE TABLE [dbo].[SiteCategory]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[SiteId] INT NOT NULL,

	-- Appears in the URL as /catalog?cat={slug}, so it is part of the store's addressable
	-- surface and should not change once a store is live.
	[Slug] NVARCHAR(80) NOT NULL,

	[Name] NVARCHAR(120) NOT NULL,
	[Blurb] NVARCHAR(400) NULL,
	[SortOrder] INT NOT NULL DEFAULT 0,
	[IsActive] BIT NOT NULL DEFAULT 1,
	[CreatedDate] DATETIME2 NOT NULL DEFAULT getutcdate(),

	CONSTRAINT [FK_SiteCategory_ToSite] FOREIGN KEY ([SiteId]) REFERENCES [Site]([Id]),
	-- Scoped by site: two stores may each want a "Networking".
	CONSTRAINT [UQ_SiteCategory_Slug] UNIQUE ([SiteId], [Slug])
)
