CREATE PROCEDURE [dbo].[spQuote_Insert]
	@Id int output,
	@Reference nvarchar(20) output,
	@AccountId int,
	@Currency nvarchar(3),
	@ExpiresDate datetime2
AS
BEGIN
	SET NOCOUNT ON;

	SET @Reference = CONCAT('QT-', FORMAT(NEXT VALUE FOR dbo.QuoteReferenceSequence, '0000'));

	INSERT INTO dbo.Quote([Reference], [AccountId], [Currency], [Status], [ExpiresDate])
	VALUES (@Reference, @AccountId, @Currency, 'Requested', @ExpiresDate);

	SELECT @Id = SCOPE_IDENTITY();
END
