CREATE TABLE [dbo].[CustomerGroup]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
    [Name] NVARCHAR(100) NOT NULL,
    [Slug] NVARCHAR(120) NOT NULL,
    [Discount] INT NOT NULL DEFAULT 0,
    [Terms] NVARCHAR(50) NOT NULL DEFAULT 'Net 30',
    [Note] NVARCHAR(500) NULL,
    [CreatedDate] DATETIME2 NOT NULL DEFAULT getutcdate(),
    CONSTRAINT [UQ_CustomerGroup_Name] UNIQUE ([Name]),
    -- The admin UI routes to /admin/groups/{slug}, so the slug must resolve to exactly one group.
    CONSTRAINT [UQ_CustomerGroup_Slug] UNIQUE ([Slug])
)
