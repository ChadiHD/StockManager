/*
Keeps what Product.Published, Featured and Badge said before the schema diff drops them.

T8 moves those three onto dbo.SiteProduct, per store, because they were store decisions on a
table every store shares. Nothing in the platform wrote them, so on most databases every row
holds its default and this saves nothing. A row somebody edited by hand should survive the
change rather than vanish with the column, and this is the only phase that can still read it.

Two halves. This one copies the non-default rows aside before the diff runs; the post-deployment
half (MoveProductFlagsToSites.sql) files them under each store once the diff has created
SiteProduct, and drops the holding table. Neither half does anything on a database that has
already moved, so both are safe on every publish.

Dynamic SQL because the columns may not exist: a static reference to a missing column fails the
batch at compile time, before the IF that would have skipped it.
*/
IF COL_LENGTH('dbo.Product', 'Published') IS NOT NULL
   AND OBJECT_ID('dbo.ProductFlagsBeforeT8', 'U') IS NULL
BEGIN
    EXEC sp_executesql N'
        SELECT [Id] AS [ProductId], [Published], [Featured], [Badge]
        INTO dbo.ProductFlagsBeforeT8
        FROM dbo.Product
        WHERE [Published] = 0 OR [Featured] = 1 OR [Badge] IS NOT NULL;';
END
GO
