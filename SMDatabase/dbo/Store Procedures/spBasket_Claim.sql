/*
Attaches the browser's basket to the contact who has just signed in, merging it into whatever
that contact already had.

This is the whole of "merged on sign-in", and it is a transaction rather than three calls
because the halfway states are all wrong: lines moved but the source basket left behind is a
duplicate the next sign-in merges again, and a source deleted before its lines are moved is a
customer's list thrown away at the moment they identified themselves.

The contact's own basket survives, not the token's. A contact signs in from several browsers
and each carries a different token; keeping the contact's basket means its id is stable and
the merge is idempotent.
*/
CREATE PROCEDURE [dbo].[spBasket_Claim]
	@SiteId int,
	@Token nvarchar(64),
	@ContactId int
AS
BEGIN
	SET NOCOUNT ON;

	-- Contact carries no SiteId of its own, only its account's, so the chain has to be walked.
	-- Without it, a cookie readable by both hosts could attach one store's basket to another
	-- store's contact — the shared Data Protection key ring makes that a real shape, not a
	-- hypothetical one.
	IF NOT EXISTS (
		SELECT 1
		FROM dbo.Contact c
		INNER JOIN dbo.Account a ON a.[Id] = c.[AccountId]
		WHERE c.[Id] = @ContactId AND a.[SiteId] = @SiteId)
	BEGIN
		THROW 50022, 'That contact does not belong to this store.', 1;
	END

	DECLARE @Theirs int, @Mine int;

	BEGIN TRY
		BEGIN TRANSACTION;

		SELECT @Mine = [Id] FROM dbo.Basket
		WHERE [ContactId] = @ContactId AND [SiteId] = @SiteId;

		SELECT @Theirs = [Id] FROM dbo.Basket
		WHERE [Token] = @Token AND [SiteId] = @SiteId AND [ContactId] IS NULL;

		IF @Theirs IS NULL
		BEGIN
			-- Nothing in the browser to merge. Whatever the contact already had stands.
			COMMIT TRANSACTION;

			SELECT @Mine AS [BasketId];

			RETURN;
		END

		IF @Mine IS NULL
		BEGIN
			-- First sign-in with this basket: it simply becomes theirs. The token stays valid
			-- so the same browser keeps reading the same basket.
			UPDATE dbo.Basket
			SET [ContactId] = @ContactId,
			    [UpdatedUtc] = SYSUTCDATETIME()
			WHERE [Id] = @Theirs;

			SET @Mine = @Theirs;
		END
		ELSE
		BEGIN
			-- Quantities add where both baskets hold the same product, which is what a
			-- customer who added it on their phone and again on their laptop means. Capped as
			-- spBasket_AddLine caps.
			UPDATE m
			SET [Quantity] = CASE WHEN m.[Quantity] + t.[Quantity] > 9999 THEN 9999
			                      ELSE m.[Quantity] + t.[Quantity] END
			FROM dbo.BasketLine m
			INNER JOIN dbo.BasketLine t
				ON t.[BasketId] = @Theirs
				AND t.[ProductId] = m.[ProductId]
			WHERE m.[BasketId] = @Mine;

			INSERT INTO dbo.BasketLine ([BasketId], [ProductId], [Quantity])
			SELECT @Mine, t.[ProductId], t.[Quantity]
			FROM dbo.BasketLine t
			WHERE t.[BasketId] = @Theirs
			  AND NOT EXISTS (SELECT 1 FROM dbo.BasketLine m
			                  WHERE m.[BasketId] = @Mine AND m.[ProductId] = t.[ProductId]);

			-- Lines go with it through FK_BasketLine_ToBasket's cascade.
			DELETE FROM dbo.Basket WHERE [Id] = @Theirs;

			UPDATE dbo.Basket SET [UpdatedUtc] = SYSUTCDATETIME() WHERE [Id] = @Mine;
		END

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		THROW;
	END CATCH

	-- The surviving basket, so the caller can reissue the cookie against its token.
	SELECT @Mine AS [BasketId];
END
