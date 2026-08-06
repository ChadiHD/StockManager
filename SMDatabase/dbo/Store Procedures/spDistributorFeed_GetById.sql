CREATE PROCEDURE [dbo].[spDistributorFeed_GetById]
	@Id int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [Id], [Name], [Host], [Port], [Username], [SecretProvider], [SecretRef],
	       [RemoteDirectory], [HostKeySha256], [Enabled],
	       [FieldSku], [FieldName], [FieldDescription], [FieldCategory],
	       [FieldCost], [FieldSrp], [FieldQuantity],
	       [LastSyncedUtc], [LastSyncStatus], [CreatedDate]
	FROM [dbo].[DistributorFeed]
	WHERE [Id] = @Id;
END
