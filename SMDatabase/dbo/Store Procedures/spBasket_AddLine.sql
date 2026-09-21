-- Adds a product to a basket, or raises the quantity of the line already there.
--
-- The product has to be visible to this viewer, checked here and not trusted from the page the
-- button was on. A ProductId arrives in a form post, so without this check a basket could be
-- filled with another store's catalog, or with products a customer group's rules hide.
CREATE PROCEDURE [dbo].[spBasket_AddLine]
	@BasketId int,
	@SiteId int,
	@ProductId int,
	@Quantity int,
	@CustomerGroupId int = NULL
AS
BEGIN
	SET NOCOUNT ON;

	IF NOT EXISTS (SELECT 1 FROM dbo.Basket WHERE [Id] = @BasketId AND [SiteId] = @SiteId)
	BEGIN
		THROW 50020, 'That basket does not belong to this store.', 1;
	END

	-- 9999 rather than int's ceiling: Quantity * NetPrice is money arithmetic, and a quantity
	-- nobody would order is a quantity that overflows a line total. A B2B buyer needing more
	-- than that is talking to sales, which is what this basket is for.
	SET @Quantity = CASE WHEN @Quantity < 1 THEN 1
	                     WHEN @Quantity > 9999 THEN 9999
	                     ELSE @Quantity END;

	-- The same degradation the catalog applies: a group from another store is treated as no
	-- group, so its visibility rules cannot decide what this store shows.
	IF @CustomerGroupId IS NOT NULL
	   AND NOT EXISTS (SELECT 1 FROM dbo.CustomerGroup
	                   WHERE [Id] = @CustomerGroupId AND [SiteId] = @SiteId)
	BEGIN
		SET @CustomerGroupId = NULL;
	END

	DECLARE @HasIncludeRule bit =
		CASE WHEN @CustomerGroupId IS NULL THEN 0
		     WHEN EXISTS (SELECT 1 FROM dbo.GroupVisibility
		                  WHERE [CustomerGroupId] = @CustomerGroupId AND [Rule] = 'IncludeCategory')
		     THEN 1 ELSE 0 END;

	DECLARE @StaleBeforeUtc datetime2 = dbo.fnSite_StaleBeforeUtc(@SiteId);

	-- Through fnCatalog_VisibleProducts so "visible" means exactly what it means on the
	-- listing, the facet rail and the detail page. Discount and margin are zeroed: no price is
	-- read here, only existence.
	IF NOT EXISTS (
		SELECT 1 FROM dbo.fnCatalog_VisibleProducts(
			@SiteId, @CustomerGroupId, @HasIncludeRule, NULL, NULL, 0, NULL, NULL, NULL, 0, 0,
			@StaleBeforeUtc)
		WHERE [Id] = @ProductId)
	BEGIN
		THROW 50021, 'That product is not available in this store.', 1;
	END

	BEGIN TRY
		BEGIN TRANSACTION;

		-- One statement so a second press of the same button cannot insert a duplicate line
		-- between a read and a write. UQ_BasketLine_Product is what makes the WHERE NOT EXISTS
		-- safe rather than merely likely: if two arrive together, one insert fails and the
		-- CATCH turns it into the update it should have been.
		UPDATE dbo.BasketLine
		SET [Quantity] = CASE WHEN [Quantity] + @Quantity > 9999 THEN 9999
		                      ELSE [Quantity] + @Quantity END
		WHERE [BasketId] = @BasketId AND [ProductId] = @ProductId;

		IF @@ROWCOUNT = 0
		BEGIN
			INSERT INTO dbo.BasketLine ([BasketId], [ProductId], [Quantity])
			VALUES (@BasketId, @ProductId, @Quantity);
		END

		UPDATE dbo.Basket SET [UpdatedUtc] = SYSUTCDATETIME() WHERE [Id] = @BasketId;

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;

		IF ERROR_NUMBER() IN (2601, 2627)
		BEGIN
			-- The concurrent-insert case. The other request created the line; this one is the
			-- quantity it was asking to add.
			UPDATE dbo.BasketLine
			SET [Quantity] = CASE WHEN [Quantity] + @Quantity > 9999 THEN 9999
			                      ELSE [Quantity] + @Quantity END
			WHERE [BasketId] = @BasketId AND [ProductId] = @ProductId;

			UPDATE dbo.Basket SET [UpdatedUtc] = SYSUTCDATETIME() WHERE [Id] = @BasketId;
		END
		ELSE
		BEGIN
			THROW;
		END
	END CATCH
END
