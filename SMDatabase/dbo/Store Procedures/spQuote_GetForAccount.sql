/*
One quote, by reference, for the account that raised it.

**A reference is not an authorisation.** They come from dbo.QuoteReferenceSequence and read
QT-0041, so scoping by site alone -- which is right for an admin and is what
spQuote_GetByReference does -- would let any signed-in customer read any other customer's
quote, their lines and their prices, by changing a digit.

Answers nothing for "not yours" exactly as it does for "not here", for the reason
spAccountDocument_GetById answers 404 rather than 403: with sequential ids, a distinguishable
refusal confirms which ones exist.
*/
CREATE PROCEDURE [dbo].[spQuote_GetForAccount]
	@Reference nvarchar(20),
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
	WHERE [q].[Reference] = @Reference
	  AND [q].[AccountId] = @AccountId
	  AND [q].[SiteId] = @SiteId
	GROUP BY [q].[Id], [q].[Reference], [q].[AccountId], [a].[Company],
	         [q].[Currency], [q].[Status], [q].[CreatedDate], [q].[ExpiresDate],
	         [q].[CustomerNote], [q].[RejectedReason];
END
