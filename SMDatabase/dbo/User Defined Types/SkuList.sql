-- A set of SKUs, for the admin's bulk actions over a selection of products.
--
-- SKUs rather than ids because the portal keys every product on its SKU; an id never reaches
-- the admin's markup either. Keyed, so a selection that names a product twice is one row.
CREATE TYPE [dbo].[SkuList] AS TABLE
(
	[Sku] NVARCHAR(50) NOT NULL PRIMARY KEY
)
