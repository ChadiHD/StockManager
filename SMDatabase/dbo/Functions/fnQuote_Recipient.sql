/*
Who the mail about a quote, or the order raised from it, goes to.

In order: the contact the caller names, if they are still an active contact on the quote's
account -- the buyer who just pressed Accept; then the buyer who asked for the quote; then the
account's own address. A disabled contact is somebody who has left the company, and their
mailbox is the wrong place for the price.

One function rather than the same three-way COALESCE in spQuote_Price, spOrder_ConvertFromQuote
and the expiry sweep, which is how the three would come to disagree about who a customer is.

Returns no row when there is nobody to tell, and the callers queue nothing, because a row with
no address can only dead-letter.
*/
CREATE FUNCTION [dbo].[fnQuote_Recipient]
(
	@QuoteId int,
	@PreferredContactId int
)
RETURNS TABLE
AS RETURN
	SELECT TOP (1) [candidate].[Email], [candidate].[Name]
	FROM (
		SELECT 1 AS [Rank], [c].[Email], CONCAT([c].[FirstName], N' ', [c].[LastName]) AS [Name]
		FROM dbo.Quote q
		INNER JOIN dbo.Contact c ON c.[Id] = @PreferredContactId AND c.[AccountId] = q.[AccountId]
		WHERE q.[Id] = @QuoteId AND c.[Status] = N'Active'

		UNION ALL

		SELECT 2, [c].[Email], CONCAT([c].[FirstName], N' ', [c].[LastName])
		FROM dbo.Quote q
		INNER JOIN dbo.Contact c ON c.[Id] = q.[RequestedByContactId] AND c.[AccountId] = q.[AccountId]
		WHERE q.[Id] = @QuoteId AND c.[Status] = N'Active'

		UNION ALL

		SELECT 3, [a].[Email], [a].[ContactName]
		FROM dbo.Quote q
		INNER JOIN dbo.Account a ON a.[Id] = q.[AccountId] AND a.[SiteId] = q.[SiteId]
		WHERE q.[Id] = @QuoteId
	) [candidate]
	WHERE NULLIF(LTRIM(RTRIM([candidate].[Email])), N'') IS NOT NULL
	ORDER BY [candidate].[Rank];
