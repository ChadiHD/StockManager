/*
Retires the second and later rows a feed holds for one of its SKUs, so UQ_Product_FeedSku can be
built.

Seed.sql's feed backfill, which runs on every publish, filed a re-added feed's old rows back under
it beside the copies its sync had made, and a feed merge without HOLDLOCK could insert one SKU
twice; either way the products screen listed the product more than once. The oldest row is kept: it is the one quotes, baskets and the store's own settings
most likely name. The others are not deleted, because a quote or an order may hold them. They
leave the feed (FeedId NULL, which sells nowhere and is never merged again), delisted with no
stock, exactly as a product a distributor dropped.

Pre-deployment because the diff builds the unique index, and fails on the rows this clears. It
does nothing on an empty database, or one that predates Product.FeedId, and nothing once the
index exists. Dynamic SQL because a static reference to a column that is not there yet fails
the batch at compile time.
*/

IF OBJECT_ID('dbo.Product', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.Product', 'FeedId') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE [object_id] = OBJECT_ID('dbo.Product') AND [name] = 'UQ_Product_FeedSku')
BEGIN
    EXEC sp_executesql N'
        WITH ranked AS (
            SELECT [FeedId], [Delisted], [QuantityInStock],
                   ROW_NUMBER() OVER (PARTITION BY [FeedId], [DistributorSku] ORDER BY [Id]) AS [Copy]
            FROM dbo.Product
            WHERE [FeedId] IS NOT NULL AND [DistributorSku] IS NOT NULL
        )
        UPDATE ranked
        SET [FeedId] = NULL, [Delisted] = 1, [QuantityInStock] = 0
        WHERE [Copy] > 1;';
END
