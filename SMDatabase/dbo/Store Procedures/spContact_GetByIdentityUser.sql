/*
The contact, account and store behind an authenticated user.

This is the lookup that makes a storefront session safe. ASP.NET Identity has no concept of a
site, and the Data Protection ring is deliberately shared between StockApi and SMStore so a
cookie issued by either is readable by both — which means a valid cookie proves who someone
is and says nothing about which store they may use. The site predicate here is what turns
that into an answer: a user registered at one store resolves to nothing at another.

Account.Status is returned rather than filtered on, so the caller can tell "no such user here"
from "your application has not been approved yet" and say something useful. It must still
refuse anything but Approved.
*/
CREATE PROCEDURE [dbo].[spContact_GetByIdentityUser]
	@IdentityUserId nvarchar(450),
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT TOP 1
	       [k].[Id], [k].[AccountId], [k].[IdentityUserId], [k].[FirstName], [k].[LastName],
	       [k].[Email], [k].[Phone], [k].[RoleInAccount], [k].[IsPrimary], [k].[Status],
	       [k].[CreatedDate],
	       [a].[CustomerGroupId], [a].[Status] AS [AccountStatus], [a].[Company] AS [AccountCompany],
	       -- The rate, not just the group. The storefront has to resolve a price in C# for
	       -- display and cannot do it from an id, and making it fetch the group separately
	       -- would be a second round trip on every authenticated page for one integer.
	       ISNULL([g].[Discount], 0) AS [CustomerGroupDiscount]
	FROM [dbo].[Contact] k
	INNER JOIN [dbo].[Account] a
		ON a.[Id] = k.[AccountId]
		AND a.[SiteId] = @SiteId
	LEFT JOIN [dbo].[CustomerGroup] g
		ON g.[Id] = a.[CustomerGroupId]
		-- FK_Account_ToCustomerGroup is composite so a cross-store pairing cannot be stored,
		-- but a discount is the last place to rely on a constraint somewhere else.
		AND g.[SiteId] = @SiteId
	WHERE [k].[IdentityUserId] = @IdentityUserId;
END
