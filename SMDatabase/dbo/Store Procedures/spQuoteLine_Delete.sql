-- Removes one line from a quote. QuoteId is part of the predicate rather than something the
-- caller checks first, so a guessed line id cannot strip a line off a different quote.
-- Returns the row count: a line that belonged elsewhere, or was already gone, reports 0 rather
-- than looking like a successful delete.
CREATE PROCEDURE [dbo].[spQuoteLine_Delete]
	@Id int,
	@QuoteId int
AS
BEGIN
	SET NOCOUNT ON;

	DELETE FROM dbo.QuoteLine
	WHERE [Id] = @Id AND [QuoteId] = @QuoteId;

	SELECT @@ROWCOUNT AS [Deleted];
END
