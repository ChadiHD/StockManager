-- @SiteId is required, not optional. An admin acts for one store at a time, and a procedure
-- that would happily return every tenant's accounts if the caller forgot is the wrong shape
-- for a security predicate: make omitting it a hard error rather than a silent leak.
CREATE PROCEDURE [dbo].[spAccount_GetAll]
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [a].[Id], [a].[Reference], [a].[Company], [a].[ContactName], [a].[Email], [a].[Country],
	       [a].[Currency], [a].[CustomerGroupId], [g].[Name] AS [GroupName],
	       [a].[PaymentMethod], [a].[PaymentTerms], [a].[CreditLimit], [a].[Status], [a].[CreatedDate],
	       [a].[VatNumber], [a].[RegistrationNumber],
	       [a].[ApprovedUtc], [a].[ApprovedBy], [a].[RejectionReason]
	FROM [dbo].[Account] a
	-- The group is scoped as well as the account. FK_Account_ToCustomerGroup is composite, so
	-- a mismatched pair cannot be stored, but the predicate costs nothing and keeps the query
	-- correct on its own terms rather than by appeal to a constraint elsewhere.
	LEFT JOIN [dbo].[CustomerGroup] g
		ON g.[Id] = a.[CustomerGroupId]
		AND g.[SiteId] = @SiteId
	WHERE [a].[SiteId] = @SiteId
	ORDER BY [a].[Company];
END
