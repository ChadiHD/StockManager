CREATE PROCEDURE [dbo].[spQuote_UpdateStatus]
	@Id int,
	@Status nvarchar(20)
AS
BEGIN
	SET NOCOUNT ON;

	UPDATE dbo.Quote
	SET [Status] = @Status
	WHERE [Id] = @Id;
END
