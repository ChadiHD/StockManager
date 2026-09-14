CREATE PROCEDURE [dbo].[spAccount_GetById]
	@Id int,
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [a].[Id], [a].[Reference], [a].[Company], [a].[ContactName], [a].[Email], [a].[Country],
	       [a].[Currency], [a].[CustomerGroupId], [g].[Name] AS [GroupName],
	       [a].[PaymentMethod], [a].[PaymentTerms], [a].[CreditLimit], [a].[Status], [a].[CreatedDate]
	FROM [dbo].[Account] a
	LEFT JOIN [dbo].[CustomerGroup] g
		ON g.[Id] = a.[CustomerGroupId]
		AND g.[SiteId] = @SiteId
	-- Site is part of the predicate, not checked afterwards: an id belonging to another store
	-- must return nothing, which the caller reads as "not found" rather than "forbidden" and
	-- so cannot be used to probe for the existence of another tenant's accounts.
	WHERE [a].[Id] = @Id
	  AND [a].[SiteId] = @SiteId;
END
