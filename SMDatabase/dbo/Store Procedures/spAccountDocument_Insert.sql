-- Writes the metadata for a file already committed to the document store.
--
-- Order matters and is the caller's responsibility: store the bytes first, then record them.
-- A row with no file behind it is a broken download; a file with no row is an orphan nobody
-- serves, which is the cheaper failure.
CREATE PROCEDURE [dbo].[spAccountDocument_Insert]
	@Id int output,
	@AccountId int,
	@Kind nvarchar(40),
	@StoredName nvarchar(100),
	@OriginalName nvarchar(260),
	@ContentType nvarchar(100),
	@SizeBytes bigint,
	@UploadedByContactId int,
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	IF NOT EXISTS (SELECT 1 FROM dbo.Account WHERE [Id] = @AccountId AND [SiteId] = @SiteId)
	BEGIN
		THROW 50005, 'That account does not belong to this store.', 1;
	END

	-- A contact from a different account would attribute one customer's upload to another.
	IF @UploadedByContactId IS NOT NULL
	   AND NOT EXISTS (SELECT 1 FROM dbo.Contact
	                   WHERE [Id] = @UploadedByContactId AND [AccountId] = @AccountId)
	BEGIN
		THROW 50006, 'That contact does not belong to this account.', 1;
	END

	INSERT INTO dbo.AccountDocument([AccountId], [Kind], [StoredName], [OriginalName],
	                                [ContentType], [SizeBytes], [UploadedByContactId])
	VALUES (@AccountId, @Kind, @StoredName, @OriginalName,
	        @ContentType, @SizeBytes, @UploadedByContactId);

	SELECT @Id = SCOPE_IDENTITY();
END
