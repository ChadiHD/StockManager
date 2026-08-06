CREATE PROCEDURE [dbo].[spDistributorFeed_Insert]
	@Id int output,
	@Name nvarchar(100),
	@Host nvarchar(200),
	@Port int,
	@Username nvarchar(100),
	@SecretProvider nvarchar(30),
	@SecretRef nvarchar(MAX),
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

	INSERT INTO dbo.DistributorFeed([Name], [Host], [Port], [Username], [SecretProvider], [SecretRef],
	                                [RemoteDirectory], [HostKeySha256], [Enabled],
	                                [FieldSku], [FieldName], [FieldDescription], [FieldCategory],
	                                [FieldCost], [FieldSrp], [FieldQuantity])
	VALUES (@Name, @Host, @Port, @Username, @SecretProvider, @SecretRef,
	        @RemoteDirectory, @HostKeySha256, @Enabled,
	        @FieldSku, @FieldName, @FieldDescription, @FieldCategory,
	        @FieldCost, @FieldSrp, @FieldQuantity);

	SELECT @Id = SCOPE_IDENTITY();
END
