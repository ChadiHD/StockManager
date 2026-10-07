CREATE TABLE [dbo].[User]
(
    [UserId] NVARCHAR(128) NOT NULL PRIMARY KEY, 
    [FirstName] NVARCHAR(50) NOT NULL, 
    [LastName] NVARCHAR(50) NOT NULL, 
    [EmailAddress] NVARCHAR(256) NOT NULL,
    [CreatedDate] DATETIME2 NOT NULL DEFAULT getutcdate(),

    -- May act for every store in the admin portal, including stores created later. Otherwise
    -- the stores in dbo.UserSite, and none if there are none. Off by default: a new colleague
    -- is given stores, not handed all of them because nobody ticked a box. Last in the table,
    -- so the publish appends rather than rebuilds; Seed.sql gives it to every user that existed
    -- before it did.
    [AllSites] BIT NOT NULL CONSTRAINT [DF_User_AllSites] DEFAULT 0
)
