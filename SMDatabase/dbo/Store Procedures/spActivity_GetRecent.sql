-- Feeds the dashboard activity list by unioning the three things that generate events:
-- account registrations, quotes and sales orders.
--
-- Every branch of the union is scoped separately. A single predicate over the result would
-- not do: the three sources reach their site by different columns, and a branch that forgot
-- one would put another store's company names on this store's dashboard.
CREATE PROCEDURE [dbo].[spActivity_GetRecent]
	@SiteId int,
	@Take int = 10
AS
BEGIN
	SET NOCOUNT ON;

	SELECT TOP (@Take) [When], [Account], [What], [Type], [Status], [Screen]
	FROM
	(
		SELECT [a].[CreatedDate] AS [When],
		       [a].[Company] AS [Account],
		       N'Account registration submitted' AS [What],
		       N'account' AS [Type],
		       [a].[Status] AS [Status],
		       N'accounts' AS [Screen]
		FROM [dbo].[Account] a
		WHERE [a].[SiteId] = @SiteId

		UNION ALL

		SELECT [q].[CreatedDate],
		       [acc].[Company],
		       CONCAT(N'Quote ', [q].[Reference]),
		       N'quote',
		       [q].[Status],
		       N'quotes'
		FROM [dbo].[Quote] q
		INNER JOIN [dbo].[Account] acc ON acc.[Id] = q.[AccountId]
		WHERE [q].[SiteId] = @SiteId

		UNION ALL

		SELECT [p].[PurchaseDate],
		       ISNULL([a2].[Company], N'—'),
		       CONCAT(N'Order ', [p].[Reference]),
		       N'order',
		       ISNULL([p].[Status], N'—'),
		       N'orders'
		FROM [dbo].[Purchase] p
		LEFT JOIN [dbo].[Account] a2 ON a2.[Id] = p.[AccountId]
		WHERE [p].[Reference] IS NOT NULL
		  AND [p].[SiteId] = @SiteId
	) AS activity
	ORDER BY [When] DESC;
END
