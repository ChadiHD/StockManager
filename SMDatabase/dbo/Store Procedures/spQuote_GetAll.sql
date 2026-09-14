CREATE PROCEDURE [dbo].[spQuote_GetAll]
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
	WHERE [q].[SiteId] = @SiteId
	GROUP BY [q].[Id], [q].[Reference], [q].[AccountId], [a].[Company],
	         [q].[Currency], [q].[Status], [q].[CreatedDate], [q].[ExpiresDate]
	ORDER BY [q].[CreatedDate] DESC;
END
