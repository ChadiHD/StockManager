CREATE PROCEDURE [dbo].[spDistributorFeed_GetAll]
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [Id], [Name], [Host], [Port], [Username], [SecretProvider], [SecretRef],
	       [RemoteDirectory], [HostKeySha256], [Enabled],
	       [FieldSku], [FieldName], [FieldDescription], [FieldCategory],
	       [FieldCost], [FieldSrp], [FieldQuantity],
	       [LastSyncedUtc], [LastSyncStatus], [CreatedDate]
	FROM [dbo].[DistributorFeed]
	ORDER BY [Name];
END
