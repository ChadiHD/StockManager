-- This viewer's basket, or nothing. Creates none — see spBasket_Ensure for why only an add
-- brings a basket into existence.
CREATE PROCEDURE [dbo].[spBasket_Find]
	@SiteId int,
	@Token nvarchar(64) = NULL,
	@ContactId int = NULL
AS
BEGIN
	SET NOCOUNT ON;

	IF @ContactId IS NOT NULL
	BEGIN
		-- A signed-in contact is found by who they are, not by what their browser is carrying.
		SELECT [Id], [SiteId], [ContactId], [Token], [CreatedUtc], [UpdatedUtc]
		FROM dbo.Basket
		WHERE [ContactId] = @ContactId AND [SiteId] = @SiteId;

		RETURN;
	END

	/*
	Anonymous, and the ContactId IS NULL predicate is the point.

	A basket claimed at sign-in keeps its token, so after sign-out the browser is still holding
	a cookie that names a basket now belonging to a customer. Without this predicate the next
	person at that machine would be shown the previous customer's list. BasketService clears
	the cookie on sign-out as well; this is the half that does not depend on a response header
	arriving.

	Site is part of the lookup too: a token is a bearer credential and says nothing about which
	store issued it.
	*/
	SELECT [Id], [SiteId], [ContactId], [Token], [CreatedUtc], [UpdatedUtc]
	FROM dbo.Basket
	WHERE [Token] = @Token
	  AND [SiteId] = @SiteId
	  AND [ContactId] IS NULL;
END
