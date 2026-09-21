-- Sets a line's quantity, or removes the line when the quantity is zero or less.
--
-- Zero is a removal rather than an error because that is what a customer typing 0 into a
-- quantity box means, and a quantity box that refuses 0 needs a separate remove button beside
-- every row.
CREATE PROCEDURE [dbo].[spBasket_SetQuantity]
	@BasketId int,
	@SiteId int,
	@ProductId int,
	@Quantity int
AS
BEGIN
	SET NOCOUNT ON;

	IF NOT EXISTS (SELECT 1 FROM dbo.Basket WHERE [Id] = @BasketId AND [SiteId] = @SiteId)
	BEGIN
		THROW 50020, 'That basket does not belong to this store.', 1;
	END

	DECLARE @Affected int;

	IF @Quantity < 1
	BEGIN
		DELETE FROM dbo.BasketLine
		WHERE [BasketId] = @BasketId AND [ProductId] = @ProductId;

		SET @Affected = @@ROWCOUNT;
	END
	ELSE
	BEGIN
		-- See spBasket_AddLine for the ceiling.
		UPDATE dbo.BasketLine
		SET [Quantity] = CASE WHEN @Quantity > 9999 THEN 9999 ELSE @Quantity END
		WHERE [BasketId] = @BasketId AND [ProductId] = @ProductId;

		SET @Affected = @@ROWCOUNT;
	END

	UPDATE dbo.Basket SET [UpdatedUtc] = SYSUTCDATETIME() WHERE [Id] = @BasketId;

	-- Captured above rather than read here, because the Basket touch always affects exactly one
	-- row and would report a change that did not happen. The caller needs to tell "nothing to
	-- change" from "changed", which is the difference between a stale page and a real edit.
	SELECT @Affected AS [Affected];
END
