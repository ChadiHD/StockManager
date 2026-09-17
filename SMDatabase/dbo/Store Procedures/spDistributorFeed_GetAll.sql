-- Returns SecretRef, which is ciphertext bound to the Data Protection ring StockApi holds.
-- That makes the site predicate below the most consequential one in the schema: without it
-- every tenant's admin receives every other tenant's encrypted distributor credentials, from
-- an API that can decrypt them.
CREATE PROCEDURE [dbo].[spDistributorFeed_GetAll]
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [Id], [SiteId], [Name], [Host], [Port], [Username], [SecretProvider], [SecretRef],
	       [RemoteDirectory], [HostKeySha256], [Enabled],
	       [FieldSku], [FieldName], [FieldDescription], [FieldCategory],
	       [FieldCost], [FieldSrp], [FieldQuantity],
	       [FieldManufacturer], [FieldMpn], [FieldEan], [FieldIcecat],
	       [LastSyncedUtc], [LastSyncStatus], [SyncStartedUtc], [CreatedDate]
	FROM [dbo].[DistributorFeed]
	WHERE [SiteId] = @SiteId
	ORDER BY [Name];
END
