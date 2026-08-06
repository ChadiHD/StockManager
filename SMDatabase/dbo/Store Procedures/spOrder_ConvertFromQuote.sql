-- Converts a quote into a sales order: copies the quote lines into PurchaseDetail, totals the
-- order, and marks the quote Accepted. Wrapped in a transaction so a partial conversion can
-- never leave an order without its lines.
CREATE PROCEDURE [dbo].[spOrder_ConvertFromQuote]
	@QuoteId int,
	@StaffId nvarchar(128),
	@Id int output,
	@Reference nvarchar(20) output
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @AccountId int, @Currency nvarchar(3);

	SELECT @AccountId = [AccountId], @Currency = [Currency]
	FROM dbo.Quote
	WHERE [Id] = @QuoteId;

	IF @AccountId IS NULL
	BEGIN
		THROW 50001, 'Quote not found.', 1;
	END

	BEGIN TRY
		BEGIN TRANSACTION;

		SET @Reference = CONCAT('SO-', FORMAT(NEXT VALUE FOR dbo.OrderReferenceSequence, '0000'));

		INSERT INTO dbo.Purchase([StaffId], [PurchaseDate], [SubTotal], [VAT], [FinalPrice],
		                         [Reference], [AccountId], [QuoteId], [Currency], [Status])
		VALUES (@StaffId, SYSUTCDATETIME(), 0, 0, 0,
		        @Reference, @AccountId, @QuoteId, @Currency, 'Awaiting payment');

		SET @Id = SCOPE_IDENTITY();

		INSERT INTO dbo.PurchaseDetail([PurchaseId], [ProductId], [Quantity], [PurchasePrice], [VAT])
		SELECT @Id, [l].[ProductId], [l].[Quantity], [l].[NetPrice], 0
		FROM dbo.QuoteLine l
		WHERE [l].[QuoteId] = @QuoteId;

		DECLARE @SubTotal money = (
			SELECT ISNULL(SUM([Quantity] * [PurchasePrice]), 0)
			FROM dbo.PurchaseDetail
			WHERE [PurchaseId] = @Id);

		UPDATE dbo.Purchase
		SET [SubTotal] = @SubTotal,
		    [VAT] = 0,
		    [FinalPrice] = @SubTotal
		WHERE [Id] = @Id;

		UPDATE dbo.Quote
		SET [Status] = 'Accepted'
		WHERE [Id] = @QuoteId;

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		THROW;
	END CATCH
END
