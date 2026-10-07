-- A set of store ids, for the stores a member of staff is given. Keyed, so a list that names a
-- store twice is one row.
CREATE TYPE [dbo].[SiteIdList] AS TABLE
(
	[SiteId] INT NOT NULL PRIMARY KEY
)
