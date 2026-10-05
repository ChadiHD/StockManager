/*
The second half of HoldProductFlags.sql: files what Product.Published, Featured and Badge said
under each store, then drops the holding table.

An unpublished product was off every store, so it becomes a Hide row on every store -- the same
behaviour, now visibly per store. Featured and Badge go only to stores whose mapping already put
the product on sale; elsewhere the product was not visible, so there was nothing to feature.

Existing rows are left alone, so a second publish, or a store that already made its own choice,
is not overwritten.
*/
IF OBJECT_ID('dbo.ProductFlagsBeforeT8', 'U') IS NOT NULL
BEGIN
    EXEC sp_executesql N'
        INSERT INTO dbo.SiteProduct ([SiteId], [ProductId], [Visibility], [Featured], [Badge])
        SELECT s.[Id], f.[ProductId],
               CASE WHEN f.[Published] = 0 THEN N''Hide'' END,
               CASE WHEN f.[Published] = 1 THEN f.[Featured] ELSE 0 END,
               CASE WHEN f.[Published] = 1 THEN f.[Badge] END
        FROM dbo.ProductFlagsBeforeT8 f
        INNER JOIN dbo.Product p ON p.[Id] = f.[ProductId]
        CROSS JOIN dbo.Site s
        LEFT JOIN dbo.CategoryMapping m
            ON m.[SiteId] = s.[Id]
            AND m.[FeedValue] = p.[Category]
        WHERE (f.[Published] = 0 OR m.[Id] IS NOT NULL)
          AND NOT EXISTS (SELECT 1 FROM dbo.SiteProduct sp
                          WHERE sp.[SiteId] = s.[Id] AND sp.[ProductId] = f.[ProductId]);

        DROP TABLE dbo.ProductFlagsBeforeT8;';
END
GO
