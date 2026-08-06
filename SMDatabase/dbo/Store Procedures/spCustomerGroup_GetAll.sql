CREATE PROCEDURE [dbo].[spCustomerGroup_GetAll]
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [g].[Id], [g].[Name], [g].[Slug], [g].[Discount], [g].[Terms], [g].[Note],
	       COUNT([a].[Id]) AS [Accounts],
	       -- "Overrides" = accounts in the group whose payment terms differ from the group default.
	       SUM(CASE WHEN [a].[Id] IS NOT NULL AND [a].[PaymentTerms] <> [g].[Terms] THEN 1 ELSE 0 END) AS [Overrides]
	FROM [dbo].[CustomerGroup] g
	LEFT JOIN [dbo].[Account] a ON a.[CustomerGroupId] = g.[Id]
	GROUP BY [g].[Id], [g].[Name], [g].[Slug], [g].[Discount], [g].[Terms], [g].[Note]
	ORDER BY [g].[Name];
END
