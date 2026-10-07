/*
The stores a member of staff may act for in the admin portal, when dbo.[User].AllSites does not
already let them act for all of them.

Read per API request by AdminSiteResolutionMiddleware rather than carried in the token: a token
lives a day, and taking a store away from somebody has to apply to their next request, not
tomorrow's. The check is this primary key.
*/
CREATE TABLE [dbo].[UserSite]
(
	[UserId] NVARCHAR(128) NOT NULL,
	[SiteId] INT NOT NULL,
	[CreatedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_UserSite_CreatedUtc] DEFAULT SYSUTCDATETIME(),

	CONSTRAINT [PK_UserSite] PRIMARY KEY ([UserId], [SiteId]),
	CONSTRAINT [FK_UserSite_ToUser] FOREIGN KEY ([UserId]) REFERENCES [User]([UserId]),
	CONSTRAINT [FK_UserSite_ToSite] FOREIGN KEY ([SiteId]) REFERENCES [Site]([Id])
)
