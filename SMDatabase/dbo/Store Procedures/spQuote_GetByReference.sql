CREATE PROCEDURE [dbo].[spQuote_GetByReference]
	@Reference nvarchar(20),
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [q].[Id], [q].[Reference], [q].[AccountId], [a].[Company] AS [AccountName],
	       [q].[Currency], [q].[Status], [q].[CreatedDate], [q].[ExpiresDate],
	       COUNT([l].[Id]) AS [Lines],
	       ISNULL(SUM([l].[Quantity] * [l].[NetPrice]), 0) AS [Value]
	FROM [dbo].[Quote] q
	INNER JOIN [dbo].[Account] a
		ON a.[Id] = q.[AccountId]
		AND a.[SiteId] = @SiteId
	LEFT JOIN [dbo].[QuoteLine] l ON l.[QuoteId] = q.[Id]
	-- UQ_Quote_Reference is global rather than per site, so a reference does name one quote
	-- everywhere. The site predicate is here to stop one store's admin opening another's by
	-- guessing QT-0041, not to disambiguate.
	WHERE [q].[Reference] = @Reference
	  AND [q].[SiteId] = @SiteId
	GROUP BY [q].[Id], [q].[Reference], [q].[AccountId], [a].[Company],
	         [q].[Currency], [q].[Status], [q].[CreatedDate], [q].[ExpiresDate];
END
