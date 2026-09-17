CREATE PROCEDURE [dbo].[spCustomerGroup_GetBySlug]
	@Slug nvarchar(120),
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [g].[Id], [g].[Name], [g].[Slug], [g].[Discount], [g].[Terms], [g].[Note],
	       COUNT([a].[Id]) AS [Accounts],
	       SUM(CASE WHEN [a].[Id] IS NOT NULL AND [a].[PaymentTerms] <> [g].[Terms] THEN 1 ELSE 0 END) AS [Overrides]
	FROM [dbo].[CustomerGroup] g
	LEFT JOIN [dbo].[Account] a
		ON a.[CustomerGroupId] = g.[Id]
		AND a.[SiteId] = @SiteId
	-- UQ_CustomerGroup_Slug is scoped by site, so a slug names at most one group per store but
	-- may well name one in each. Without the site the lookup is ambiguous, not merely leaky.
	WHERE [g].[Slug] = @Slug
	  AND [g].[SiteId] = @SiteId
	GROUP BY [g].[Id], [g].[Name], [g].[Slug], [g].[Discount], [g].[Terms], [g].[Note];
END
