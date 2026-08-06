CREATE PROCEDURE [dbo].[spDistributorFeed_UpdateSecret]
	@Id int,
	@SecretProvider nvarchar(30),
	@SecretRef nvarchar(MAX)
AS
BEGIN
	SET NOCOUNT ON;

	UPDATE dbo.DistributorFeed
	SET [SecretProvider] = @SecretProvider,
	    [SecretRef] = @SecretRef,
	    [LastModified] = SYSUTCDATETIME()
	WHERE [Id] = @Id;
END
