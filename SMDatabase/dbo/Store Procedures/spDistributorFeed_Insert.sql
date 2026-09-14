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
	@FieldQuantity nvarchar(100),
	@FieldManufacturer nvarchar(100),
	@FieldMpn nvarchar(100),
	@FieldEan nvarchar(100),
	@FieldIcecat nvarchar(100),
	@SiteId int = NULL
AS
BEGIN
	SET NOCOUNT ON;

	-- Resolved rather than left NULL; see dbo.fnSite_Resolve. UQ_DistributorFeed_Name is
	-- scoped by site, so a NULL here is what stops two stores from each having a "Main" feed.
	SET @SiteId = [dbo].[fnSite_Resolve](@SiteId);

	IF @SiteId IS NULL
	BEGIN
		THROW 50002, 'SiteId is required: this database has more than one site, so the store to file this row under cannot be inferred.', 1;
	END

	INSERT INTO dbo.DistributorFeed([Name], [Host], [Port], [Username], [SecretProvider], [SecretRef],
	                                [RemoteDirectory], [HostKeySha256], [Enabled],
	                                [FieldSku], [FieldName], [FieldDescription], [FieldCategory],
	                                [FieldCost], [FieldSrp], [FieldQuantity],
	                                [FieldManufacturer], [FieldMpn], [FieldEan], [FieldIcecat],
	                                [SiteId])
	VALUES (@Name, @Host, @Port, @Username, @SecretProvider, @SecretRef,
	        @RemoteDirectory, @HostKeySha256, @Enabled,
	        @FieldSku, @FieldName, @FieldDescription, @FieldCategory,
	        @FieldCost, @FieldSrp, @FieldQuantity,
	        @FieldManufacturer, @FieldMpn, @FieldEan, @FieldIcecat,
	        @SiteId);

	SELECT @Id = SCOPE_IDENTITY();
END
