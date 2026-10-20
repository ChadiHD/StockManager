-- Option values (T11): a new variant's own, or the values a shopper filtered the catalogue by.
--
-- One type for both, because both are (name, value) pairs in the store's own words. A variant
-- carries one value per name, which spProduct_AddVariant checks; a filter may carry several
-- values for one name (M or L), which is why the key is the pair.
CREATE TYPE [dbo].[ProductOptionList] AS TABLE
(
	[Name] NVARCHAR(50) NOT NULL,
	[Value] NVARCHAR(50) NOT NULL,
	[SortOrder] INT NOT NULL DEFAULT 0,
	PRIMARY KEY ([Name], [Value])
)
