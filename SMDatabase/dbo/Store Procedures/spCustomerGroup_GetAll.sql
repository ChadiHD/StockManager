CREATE PROCEDURE [dbo].[spCustomerGroup_GetAll]
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [g].[Id], [g].[Name], [g].[Slug], [g].[Discount], [g].[Terms], [g].[Note],
	       COUNT([a].[Id]) AS [Accounts],
	       -- "Overrides" = accounts in the group whose payment terms differ from the group default.
	       SUM(CASE WHEN [a].[Id] IS NOT NULL AND [a].[PaymentTerms] <> [g].[Terms] THEN 1 ELSE 0 END) AS [Overrides]
	FROM [dbo].[CustomerGroup] g
	-- The counted accounts are scoped too, or a store's group would report a headcount that
	-- includes members its operator cannot open.
	LEFT JOIN [dbo].[Account] a
		ON a.[CustomerGroupId] = g.[Id]
		AND a.[SiteId] = @SiteId
	WHERE [g].[SiteId] = @SiteId
	GROUP BY [g].[Id], [g].[Name], [g].[Slug], [g].[Discount], [g].[Terms], [g].[Note]
	ORDER BY [g].[Name];
END
