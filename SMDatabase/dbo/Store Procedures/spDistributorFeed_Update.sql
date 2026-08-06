-- Updates everything except the secret. Leaving the credential out means an edit that does
-- not re-enter the password cannot accidentally blank it; spDistributorFeed_UpdateSecret is
-- the only way to change it.
CREATE PROCEDURE [dbo].[spDistributorFeed_Update]
	@Id int,
	@Name nvarchar(100),
	@Host nvarchar(200),
	@Port int,
	@Username nvarchar(100),
	@RemoteDirectory nvarchar(400),
	@HostKeySha256 nvarchar(200),
	@Enabled bit,
	@FieldSku nvarchar(100),
	@FieldName nvarchar(100),
	@FieldDescription nvarchar(100),
	@FieldCategory nvarchar(100),
	@FieldCost nvarchar(100),
	@FieldSrp nvarchar(100),
	@FieldQuantity nvarchar(100)
AS
BEGIN
	SET NOCOUNT ON;

	UPDATE dbo.DistributorFeed
	SET [Name] = @Name,
	    [Host] = @Host,
	    [Port] = @Port,
	    [Username] = @Username,
	    [RemoteDirectory] = @RemoteDirectory,
	    [HostKeySha256] = @HostKeySha256,
	    [Enabled] = @Enabled,
	    [FieldSku] = @FieldSku,
	    [FieldName] = @FieldName,
	    [FieldDescription] = @FieldDescription,
	    [FieldCategory] = @FieldCategory,
	    [FieldCost] = @FieldCost,
	    [FieldSrp] = @FieldSrp,
	    [FieldQuantity] = @FieldQuantity,
	    [LastModified] = SYSUTCDATETIME()
	WHERE [Id] = @Id;
END
