CREATE PROCEDURE [dbo].[spDistributorFeed_UpdateSecret]
	@Id int,
	@SecretProvider nvarchar(30),
	@SecretRef nvarchar(MAX),
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	UPDATE dbo.DistributorFeed
	SET [SecretProvider] = @SecretProvider,
	    [SecretRef] = @SecretRef,
	    [LastModified] = SYSUTCDATETIME()
	WHERE [Id] = @Id
	  AND [SiteId] = @SiteId;
END
