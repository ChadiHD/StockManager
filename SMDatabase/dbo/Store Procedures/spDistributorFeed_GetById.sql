CREATE PROCEDURE [dbo].[spDistributorFeed_GetById]
	@Id int,
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
	WHERE [Id] = @Id
	  AND [SiteId] = @SiteId;
END
