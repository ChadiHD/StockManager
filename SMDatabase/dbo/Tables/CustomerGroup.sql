CREATE TABLE [dbo].[CustomerGroup]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
    [Name] NVARCHAR(100) NOT NULL,
    [Slug] NVARCHAR(120) NOT NULL,
    [Discount] INT NOT NULL DEFAULT 0,
    [Terms] NVARCHAR(50) NOT NULL DEFAULT 'Net 30',
    [Note] NVARCHAR(500) NULL,
    [CreatedDate] DATETIME2 NOT NULL DEFAULT getutcdate(),
    -- Pricing groups are per store. NOT NULL: every write path sets it, and a NULL here would
    -- silently switch off FK_Account_ToCustomerGroup, which is not enforced when any of its
    -- columns is NULL.
    [SiteId] INT NOT NULL,
    -- Scoped by site, not global: two stores may each want a "Reseller" group. The admin UI
    -- routes to /admin/groups/{slug}, so the slug must resolve to exactly one group per site.
    CONSTRAINT [UQ_CustomerGroup_Name] UNIQUE ([SiteId], [Name]),
    CONSTRAINT [UQ_CustomerGroup_Slug] UNIQUE ([SiteId], [Slug]),
    -- Redundant as a key — Id is already unique — and present only so Account can hang a
    -- composite foreign key off it. See FK_Account_ToCustomerGroup.
    CONSTRAINT [UQ_CustomerGroup_IdSite] UNIQUE ([Id], [SiteId]),
    CONSTRAINT [FK_CustomerGroup_ToSite] FOREIGN KEY ([SiteId]) REFERENCES [Site]([Id])
)
