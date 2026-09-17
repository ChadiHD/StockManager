-- Takes one product out of a basket. Keyed on the product rather than the line id, so a
-- storefront form posts the SKU it is already rendering instead of a database id.
CREATE PROCEDURE [dbo].[spBasket_RemoveLine]
	@BasketId int,
	@SiteId int,
	@ProductId int
AS
BEGIN
	SET NOCOUNT ON;

	IF NOT EXISTS (SELECT 1 FROM dbo.Basket WHERE [Id] = @BasketId AND [SiteId] = @SiteId)
	BEGIN
		THROW 50020, 'That basket does not belong to this store.', 1;
	END

	DELETE FROM dbo.BasketLine
	WHERE [BasketId] = @BasketId AND [ProductId] = @ProductId;

	DECLARE @Removed int = @@ROWCOUNT;

	UPDATE dbo.Basket SET [UpdatedUtc] = SYSUTCDATETIME() WHERE [Id] = @BasketId;

	SELECT @Removed AS [Affected];
END
