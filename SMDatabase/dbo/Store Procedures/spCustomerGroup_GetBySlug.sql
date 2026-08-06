CREATE PROCEDURE [dbo].[spCustomerGroup_GetBySlug]
	@Slug nvarchar(120)
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [g].[Id], [g].[Name], [g].[Slug], [g].[Discount], [g].[Terms], [g].[Note],
	       COUNT([a].[Id]) AS [Accounts],
	       SUM(CASE WHEN [a].[Id] IS NOT NULL AND [a].[PaymentTerms] <> [g].[Terms] THEN 1 ELSE 0 END) AS [Overrides]
	FROM [dbo].[CustomerGroup] g
	LEFT JOIN [dbo].[Account] a ON a.[CustomerGroupId] = g.[Id]
	WHERE [g].[Slug] = @Slug
	GROUP BY [g].[Id], [g].[Name], [g].[Slug], [g].[Discount], [g].[Terms], [g].[Note];
END
