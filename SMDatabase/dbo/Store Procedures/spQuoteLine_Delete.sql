-- Removes one line from a quote. QuoteId and SiteId are part of the predicate rather than
-- something the caller checks first, so neither a guessed line id nor a guessed quote id can
-- strip a line off a different quote or a different store's quote.
-- Returns the row count: a line that belonged elsewhere, or was already gone, reports 0 rather
-- than looking like a successful delete.
CREATE PROCEDURE [dbo].[spQuoteLine_Delete]
	@Id int,
	@QuoteId int,
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	DELETE [l]
	FROM dbo.QuoteLine l
	INNER JOIN dbo.Quote q
		ON q.[Id] = l.[QuoteId]
		AND q.[SiteId] = @SiteId
	WHERE [l].[Id] = @Id
	  AND [l].[QuoteId] = @QuoteId;

	SELECT @@ROWCOUNT AS [Deleted];
END
