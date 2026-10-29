/*
Moves a portal order to a new status, and puts back the stock a cancelled cart order held (T12).

An order placed from a cart (QuoteId NULL) took its own stock in spOrder_PlaceFromBasket.
Cancelling it returns that stock, in the statement that claims the change, so two cancels cannot
both return it. Reopening one is refused: it would take the stock a second time with no check
that it is still there. An order from a quote never took stock, and moves as it always did.
*/
CREATE PROCEDURE [dbo].[spOrder_UpdateStatus]
	@Id int,
	@Status nvarchar(30),
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	BEGIN TRY
		BEGIN TRANSACTION;

		DECLARE @Changed TABLE ([QuoteId] int NULL, [From] nvarchar(30) NULL);

		UPDATE dbo.Purchase
		SET [Status] = @Status
		OUTPUT inserted.[QuoteId], deleted.[Status] INTO @Changed
		WHERE [Id] = @Id
		  AND [Reference] IS NOT NULL
		  AND [SiteId] = @SiteId
		  -- The refusal is in the claim, so a cancel landing between a check and this cannot
		  -- reopen an order whose stock it has just returned.
		  AND NOT (@Status <> N'Cancelled' AND [QuoteId] IS NULL AND [Status] = N'Cancelled');

		IF @@ROWCOUNT = 0
		   AND EXISTS (SELECT 1 FROM dbo.Purchase
		               WHERE [Id] = @Id AND [SiteId] = @SiteId AND [Reference] IS NOT NULL
		                 AND [QuoteId] IS NULL AND [Status] = N'Cancelled')
		BEGIN
			THROW 50136, 'A cancelled order placed from the cart cannot be reopened. Its stock has been returned.', 1;
		END

		-- Own stock only, as spOrder_PlaceFromBasket took it.
		IF @Status = N'Cancelled'
		   AND EXISTS (SELECT 1 FROM @Changed
		               WHERE [QuoteId] IS NULL AND ISNULL([From], N'') <> N'Cancelled')
		BEGIN
			UPDATE p
			SET [QuantityInStock] = p.[QuantityInStock] + d.[Quantity]
			FROM dbo.Product p
			INNER JOIN (SELECT [ProductId], SUM([Quantity]) AS [Quantity]
			            FROM dbo.PurchaseDetail
			            WHERE [PurchaseId] = @Id
			            GROUP BY [ProductId]) d ON d.[ProductId] = p.[Id]
			WHERE p.[FeedId] IS NULL
			  AND ISNULL(p.[Source], N'Own') <> N'Distributor';
		END

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		THROW;
	END CATCH
END
