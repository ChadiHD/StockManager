CREATE PROCEDURE [dbo].[spAccount_GetById]
	@Id int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [a].[Id], [a].[Reference], [a].[Company], [a].[ContactName], [a].[Email], [a].[Country],
	       [a].[Currency], [a].[CustomerGroupId], [g].[Name] AS [GroupName],
	       [a].[PaymentMethod], [a].[PaymentTerms], [a].[CreditLimit], [a].[Status], [a].[CreatedDate]
	FROM [dbo].[Account] a
	LEFT JOIN [dbo].[CustomerGroup] g ON g.[Id] = a.[CustomerGroupId]
	WHERE [a].[Id] = @Id;
END
