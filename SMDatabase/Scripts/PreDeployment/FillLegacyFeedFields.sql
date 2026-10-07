/*
Writes FlexIT's field names into the blank field mappings of feeds created before T9, once.

Until T9 a blank mapping was read as FlexIT's element name (DistributorFeedModel.ToSettings), so
every feed silently depended on FlexIT's vocabulary. A second distributor's feed with a blank
field would have read whatever its file had under FlexIT's name. From T9 a blank field is
unmapped — not read at all — and this keeps every existing feed reading exactly what it read
before, by writing the names it was falling back to.

Once, and it has to be pre-deployment to know when once is. It cannot be in Seed.sql, which runs
on every publish: there it would overwrite a UK feed's deliberately blank field with FlexIT's
names. Product.FeedId arrives in the same release, so a database without it predates T9 and its
feeds were all created under the fallback. After this publish the column exists and the step
never matches again. An empty database has no feed table and is skipped.
*/

IF OBJECT_ID('dbo.DistributorFeed', 'U') IS NOT NULL
   AND OBJECT_ID('dbo.Product', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.Product', 'FeedId') IS NULL
BEGIN
    UPDATE dbo.DistributorFeed
    SET [FieldDescription]  = ISNULL(NULLIF(LTRIM(RTRIM([FieldDescription])), N''),  N'WebDescription'),
        [FieldCategory]     = ISNULL(NULLIF(LTRIM(RTRIM([FieldCategory])), N''),     N'Category'),
        [FieldCost]         = ISNULL(NULLIF(LTRIM(RTRIM([FieldCost])), N''),         N'SalesPrice'),
        [FieldSrp]          = ISNULL(NULLIF(LTRIM(RTRIM([FieldSrp])), N''),          N'SRP'),
        [FieldQuantity]     = ISNULL(NULLIF(LTRIM(RTRIM([FieldQuantity])), N''),     N'StockQuantity'),
        [FieldManufacturer] = ISNULL(NULLIF(LTRIM(RTRIM([FieldManufacturer])), N''), N'Manufacturer'),
        [FieldMpn]          = ISNULL(NULLIF(LTRIM(RTRIM([FieldMpn])), N''),          N'ManufacturerPartNumber'),
        [FieldEan]          = ISNULL(NULLIF(LTRIM(RTRIM([FieldEan])), N''),          N'EAN'),
        [FieldIcecat]       = ISNULL(NULLIF(LTRIM(RTRIM([FieldIcecat])), N''),       N'IceCatID');
END
