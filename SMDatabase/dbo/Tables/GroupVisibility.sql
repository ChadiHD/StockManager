-- Per-customer-group catalog restrictions. Some distributor lines may not be shown to some
-- groups — a reseller agreement, an exclusivity, or a line a store simply does not want a
-- given segment quoting.
--
-- Rules are evaluated inside the catalog query, never applied to an already-fetched page: a
-- product filtered out afterwards has still been counted, still shifted the paging, and has
-- usually already reached the browser.
CREATE TABLE [dbo].[GroupVisibility]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[CustomerGroupId] INT NOT NULL,

	-- 'IncludeCategory' | 'ExcludeCategory' | 'ExcludeDistributor'
	--
	-- Include and exclude do not mix arbitrarily: when a group has any IncludeCategory rule,
	-- that becomes an allow-list and everything unlisted is hidden. Exclusions then subtract
	-- from it. Stated here because the precedence lives in spCatalog_Search and is invisible
	-- from the data alone.
	[Rule] NVARCHAR(30) NOT NULL,

	-- A SiteCategory slug for the category rules, a dbo.Product.Distributor name for the
	-- distributor rule.
	[Value] NVARCHAR(120) NOT NULL,

	[CreatedDate] DATETIME2 NOT NULL DEFAULT getutcdate(),

	CONSTRAINT [FK_GroupVisibility_ToCustomerGroup] FOREIGN KEY ([CustomerGroupId]) REFERENCES [CustomerGroup]([Id]),
	CONSTRAINT [UQ_GroupVisibility_Rule] UNIQUE ([CustomerGroupId], [Rule], [Value])
)
