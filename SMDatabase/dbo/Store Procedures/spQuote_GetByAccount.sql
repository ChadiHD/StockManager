/*
One account's quotes, for the customer's own list.

Its own procedure rather than spQuote_GetAll with an extra parameter, because the predicate is
different in kind: the admin list is scoped by site because an admin may see every quote in
their store, and this is scoped by account because a customer may see only their own. Sharing
one procedure would mean one caller passing NULL for the predicate that protects the other.
*/
CREATE PROCEDURE [dbo].[spQuote_GetByAccount]
	@AccountId int,
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [q].[Id], [q].[Reference], [q].[AccountId], [a].[Company] AS [AccountName],
	       [q].[Currency], [q].[Status], [q].[CreatedDate], [q].[ExpiresDate],
	       [q].[CustomerNote], [q].[RejectedReason],
	       COUNT([l].[Id]) AS [Lines],
	       ISNULL(SUM([l].[Quantity] * [l].[NetPrice]), 0) AS [Value]
	FROM [dbo].[Quote] q
	INNER JOIN [dbo].[Account] a
		ON a.[Id] = q.[AccountId]
		AND a.[SiteId] = @SiteId
	LEFT JOIN [dbo].[QuoteLine] l ON l.[QuoteId] = q.[Id]
	WHERE [q].[AccountId] = @AccountId
	  AND [q].[SiteId] = @SiteId
	GROUP BY [q].[Id], [q].[Reference], [q].[AccountId], [a].[Company],
	         [q].[Currency], [q].[Status], [q].[CreatedDate], [q].[ExpiresDate],
	         [q].[CustomerNote], [q].[RejectedReason]
	ORDER BY [q].[CreatedDate] DESC;
END
