-- Records a document whose bytes are already stored. The caller writes the blob first and this
-- second, and deletes the blob if this fails, as for photographs.
CREATE PROCEDURE [dbo].[spProductDocument_Add]
	@ProductId int,
	@StoredName nvarchar(64),
	@FileName nvarchar(120),
	@ContentType nvarchar(100),
	@SizeBytes bigint
AS
BEGIN
	SET NOCOUNT ON;

	INSERT INTO dbo.ProductDocument([ProductId], [StoredName], [FileName], [ContentType], [SizeBytes])
	VALUES (@ProductId, @StoredName, @FileName, @ContentType, @SizeBytes);
END
