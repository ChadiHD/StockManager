-- Finds this viewer's basket, creating one if they have none.
--
-- Called only when something is being added. A basket row per page view would mean a row per
-- crawler, and spBasket_PurgeAbandoned would spend its life cleaning up after robots.
CREATE PROCEDURE [dbo].[spBasket_Ensure]
	@SiteId int,
	@Token nvarchar(64),
	-- The signed-in contact, or NULL.
	@ContactId int = NULL
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @Id int;

	/*
	Two lookups, never one, and a signed-in contact never matches on the token.

	Adopting a token's basket for a signed-in contact is spBasket_Claim's job, which runs once
	at sign-in and merges rather than overwrites. Doing it here as well would mean an add could
	silently attach a basket somebody else built on this browser, and the predicate that stops
	an anonymous visitor reading a claimed basket (see spBasket_Find) would be undone from the
	other side.
	*/
	IF @ContactId IS NOT NULL
	BEGIN
		SELECT @Id = [Id] FROM dbo.Basket
		WHERE [ContactId] = @ContactId AND [SiteId] = @SiteId;
	END
	ELSE
	BEGIN
		SELECT @Id = [Id] FROM dbo.Basket
		WHERE [Token] = @Token AND [SiteId] = @SiteId AND [ContactId] IS NULL;
	END

	IF @Id IS NULL
	BEGIN
		BEGIN TRY
			INSERT INTO dbo.Basket ([SiteId], [ContactId], [Token])
			VALUES (@SiteId, @ContactId, @Token);

			SET @Id = SCOPE_IDENTITY();
		END TRY
		BEGIN CATCH
			-- Two requests for the same viewer arriving together: UQ_Basket_Token or
			-- UQ_Basket_Contact refuses the second, and the row the first one wrote is the
			-- answer both should get. Anything else is a real failure and is rethrown.
			IF ERROR_NUMBER() NOT IN (2601, 2627) THROW;

			IF @ContactId IS NOT NULL
			BEGIN
				SELECT @Id = [Id] FROM dbo.Basket
				WHERE [ContactId] = @ContactId AND [SiteId] = @SiteId;
			END
			ELSE
			BEGIN
				SELECT @Id = [Id] FROM dbo.Basket
				WHERE [Token] = @Token AND [SiteId] = @SiteId AND [ContactId] IS NULL;
			END
		END CATCH
	END

	SELECT [Id], [SiteId], [ContactId], [Token], [CreatedUtc], [UpdatedUtc]
	FROM dbo.Basket
	WHERE [Id] = @Id;
END
