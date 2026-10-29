/*
Turns a cart into an order (T12): takes the stock, writes the order at 'Awaiting payment' with
its lines and its delivery address, and deletes the cart. For a guest it first writes the
consumer account and contact the order belongs to.

One transaction, for spQuote_SubmitRequest's reason. An order with no lines, stock taken for an
order that was never written, or a cart left full after its order exists are each wrong.

The rules are spQuote_SubmitRequest's too. The account is derived from the contact and the
currency from the store, never taken from the caller, and every product is re-checked against
the store's placement. Prices are stated, from PriceResolver through CatalogPresenter, so the
order records what the checkout page showed.

**A short order writes nothing and is not an error.** It returns the SKUs it is short of, so
the page can name them. Thrown, the caller would have to parse them back out of a message.
*/
CREATE PROCEDURE [dbo].[spOrder_PlaceFromBasket]
	@SiteId int,
	@BasketId int,
	@Lines [dbo].[QuoteRequestLine] READONLY,

	-- The signed-in contact, or NULL for a guest. A guest gives the four after it.
	@ContactId int = NULL,
	@Email nvarchar(256) = NULL,
	@FirstName nvarchar(100) = NULL,
	@LastName nvarchar(100) = NULL,
	@Phone nvarchar(50) = NULL,

	-- Where it goes. Snapshotted into dbo.PurchaseAddress.
	@Line1 nvarchar(200),
	@Line2 nvarchar(200) = NULL,
	@City nvarchar(100),
	@Region nvarchar(100) = NULL,
	@PostCode nvarchar(20) = NULL,
	@Country nvarchar(2),

	-- Assessed by ITaxRuleSet on the delivery country, as spOrder_ConvertFromQuote's are.
	@TaxTreatment nvarchar(30) = NULL,
	@TaxLegend nvarchar(200) = NULL,
	@TaxRatePct decimal(5, 2) = 0,

	-- A guest's way back to the order. Hashed here and by spOrder_GetByGuestToken, as
	-- nvarchar both times: HASHBYTES hashes the bytes it is given, and the two must agree.
	@GuestToken nvarchar(64) = NULL,

	-- Delivery, chosen from the store's rates by the caller (T13), on the store's basis like the
	-- lines' prices. NULL and zero charge nothing.
	@ShippingName nvarchar(100) = NULL,
	@ShippingPrice money = 0
AS
BEGIN
	SET NOCOUNT ON;

	IF NOT EXISTS (SELECT 1 FROM @Lines)
	BEGIN
		THROW 50130, 'An order needs at least one line.', 1;
	END

	IF NULLIF(LTRIM(RTRIM(@Line1)), N'') IS NULL
	   OR NULLIF(LTRIM(RTRIM(@City)), N'') IS NULL
	   OR NULLIF(LTRIM(RTRIM(@Country)), N'') IS NULL
	BEGIN
		THROW 50131, 'An order needs a delivery address: a first line, a city and a country.', 1;
	END

	DECLARE @Currency nvarchar(3), @Inclusive bit;

	SELECT @Currency = [CurrencyCode], @Inclusive = [PricesIncludeTax]
	FROM dbo.Site
	WHERE [Id] = @SiteId AND [IsActive] = 1;

	IF @Currency IS NULL
	BEGIN
		THROW 50132, 'No active store with that id.', 1;
	END

	DECLARE @AccountId int, @SignedIn bit = CASE WHEN @ContactId IS NULL THEN 0 ELSE 1 END;

	IF @SignedIn = 1
	BEGIN
		-- Walked as spQuote_SubmitRequest walks it. Another store's contact reads as not found.
		SELECT @AccountId = a.[Id]
		FROM dbo.Contact c
		INNER JOIN dbo.Account a ON a.[Id] = c.[AccountId]
		WHERE c.[Id] = @ContactId
		  AND a.[SiteId] = @SiteId;

		IF @AccountId IS NULL
		BEGIN
			THROW 50133, 'That contact does not belong to this store.', 1;
		END
	END
	ELSE IF NULLIF(LTRIM(RTRIM(@Email)), N'') IS NULL
	     OR NULLIF(LTRIM(RTRIM(@FirstName)), N'') IS NULL
	     OR NULLIF(LTRIM(RTRIM(@LastName)), N'') IS NULL
	     OR @GuestToken IS NULL
	BEGIN
		THROW 50134, 'A guest order needs a name, an email address and a token.', 1;
	END

	-- Sold here, and not a style: spBasket_AddLine refuses a style with variants, and this does
	-- not depend on it having done so.
	IF EXISTS (
		SELECT 1 FROM @Lines l
		WHERE NOT EXISTS (
			SELECT 1
			FROM dbo.Product p
			CROSS APPLY dbo.fnSite_ProductPlacement(@SiteId, ISNULL(p.[ParentProductId], p.[Id]), p.[Category], p.[FeedId], p.[Source], p.[CurrencyCode], p.[PricesIncludeTax]) pl
			WHERE p.[Id] = l.[ProductId]
			  AND pl.[OnStore] = 1)
		   OR EXISTS (SELECT 1 FROM dbo.Product v WHERE v.[ParentProductId] = l.[ProductId]))
	BEGIN
		THROW 50135, 'One of those products is not sold by this store.', 1;
	END

	DECLARE @Reference nvarchar(20), @Id int;

	BEGIN TRY
		BEGIN TRANSACTION;

		/*
		The stock, read under UPDLOCK before anything is written.

		Own stock only, in fnSite_ProductSellable's words: no feed, and not marked Distributor.
		A feed's quantity is the distributor's, and its next sync writes over whatever this
		took off it.

		UPDLOCK holds to the end of the transaction, and a second checkout reading the same row
		waits for it, so two checkouts for the last shirt cannot both see one left. Reading
		first and then taking it is a race without the lock; with it, the shortage can be named
		before anything has been written, and nothing has to be undone.
		*/
		DECLARE @Stock TABLE ([Sku] nvarchar(50), [Held] int, [Wanted] int);

		INSERT INTO @Stock([Sku], [Held], [Wanted])
		SELECT p.[Sku], p.[QuantityInStock], l.[Quantity]
		FROM @Lines l
		INNER JOIN dbo.Product p WITH (UPDLOCK, ROWLOCK) ON p.[Id] = l.[ProductId]
		WHERE p.[FeedId] IS NULL
		  AND ISNULL(p.[Source], N'Own') <> N'Distributor';

		DECLARE @Short nvarchar(max) = (
			SELECT STRING_AGG([Sku], N',') WITHIN GROUP (ORDER BY [Sku])
			FROM @Stock
			WHERE [Held] < [Wanted]);

		IF @Short IS NOT NULL
		BEGIN
			-- Nothing written, so committing releases the locks and changes nothing. Not a
			-- ROLLBACK: inside a caller's transaction that would unwind the caller's too.
			COMMIT TRANSACTION;

			SELECT CAST(NULL AS nvarchar(20)) AS [Reference], @Short AS [ShortSkus];
			RETURN;
		END

		UPDATE p
		SET [QuantityInStock] = p.[QuantityInStock] - l.[Quantity]
		FROM dbo.Product p
		INNER JOIN @Lines l ON l.[ProductId] = p.[Id]
		WHERE p.[FeedId] IS NULL
		  AND ISNULL(p.[Source], N'Own') <> N'Distributor';

		/*
		A guest's account: a consumer, approved and prepaid, with one contact who has no login.

		A new one per order, never one found by email. Finding one would tell whoever typed an
		address whether its owner shops here, and file this order under somebody else's
		account. Approved, because there is nothing for anybody to review: it extends no credit.
		*/
		IF @SignedIn = 0
		BEGIN
			DECLARE @Name nvarchar(200) = LTRIM(RTRIM(CONCAT(LTRIM(RTRIM(@FirstName)), N' ', LTRIM(RTRIM(@LastName)))));

			INSERT INTO dbo.Account([Reference], [Company], [ContactName], [Email], [Country],
			                        [Currency], [PaymentMethod], [PaymentTerms], [CreditLimit],
			                        [Status], [ApprovedUtc], [SiteId], [Kind])
			VALUES (CONCAT('AC-', FORMAT(NEXT VALUE FOR dbo.AccountReferenceSequence, '0000')),
			        @Name, @Name, LTRIM(RTRIM(@Email)), UPPER(@Country),
			        @Currency, N'Card', N'Prepaid', 0,
			        N'Approved', SYSUTCDATETIME(), @SiteId, N'Consumer');

			SET @AccountId = SCOPE_IDENTITY();

			INSERT INTO dbo.Contact([AccountId], [IdentityUserId], [FirstName], [LastName],
			                        [Email], [Phone], [RoleInAccount], [IsPrimary], [Status])
			VALUES (@AccountId, NULL, LTRIM(RTRIM(@FirstName)), LTRIM(RTRIM(@LastName)),
			        LTRIM(RTRIM(@Email)), NULLIF(LTRIM(RTRIM(@Phone)), N''), N'Buyer', 1, N'Active');

			SET @ContactId = SCOPE_IDENTITY();
		END

		SET @Reference = CONCAT('SO-', FORMAT(NEXT VALUE FOR dbo.OrderReferenceSequence, '0000'));

		-- Prepaid, so due the day it is placed. Awaiting payment until T13's payment moves it.
		INSERT INTO dbo.Purchase([StaffId], [PlacedByContactId], [PurchaseDate],
		                         [SubTotal], [VAT], [FinalPrice],
		                         [Reference], [AccountId], [QuoteId], [Currency], [Status],
		                         [SiteId], [TaxTreatment], [TaxLegend], [DueDate], [GuestTokenHash])
		VALUES (NULL, @ContactId, SYSUTCDATETIME(), 0, 0, 0,
		        @Reference, @AccountId, NULL, @Currency, N'Awaiting payment',
		        @SiteId, @TaxTreatment, @TaxLegend, CAST(SYSUTCDATETIME() AS date),
		        CASE WHEN @SignedIn = 0 THEN HASHBYTES('SHA2_256', @GuestToken) END);

		SET @Id = SCOPE_IDENTITY();

		INSERT INTO dbo.PurchaseDetail([PurchaseId], [ProductId], [Quantity], [PurchasePrice],
		                               [VAT], [TaxRatePct])
		SELECT @Id, [l].[ProductId], [l].[Quantity], [l].[NetPrice], [t].[Vat], [t].[RatePct]
		FROM @Lines l
		INNER JOIN dbo.Product p ON p.[Id] = [l].[ProductId]
		CROSS APPLY dbo.fnOrder_LineTax([l].[Quantity], [l].[NetPrice], @TaxRatePct, [p].[IsTaxable], @Inclusive) t;

		-- Delivery (T13) is taxed with the goods, at the order's rate, as a line of one would be.
		DECLARE @ShippingVat money = (
			SELECT [Vat] FROM dbo.fnOrder_LineTax(1, @ShippingPrice, @TaxRatePct, 1, @Inclusive));

		-- Summed from the lines, as spOrder_ConvertFromQuote sums them, with the delivery, and net
		-- of tax on either basis.
		DECLARE @Vat money = (SELECT ISNULL(SUM([VAT]), 0) FROM dbo.PurchaseDetail WHERE [PurchaseId] = @Id) + @ShippingVat;
		DECLARE @Gross money = (SELECT ISNULL(SUM([Quantity] * [PurchasePrice]), 0) FROM dbo.PurchaseDetail WHERE [PurchaseId] = @Id) + @ShippingPrice;
		DECLARE @SubTotal money = @Gross - CASE WHEN @Inclusive = 1 THEN @Vat ELSE 0 END;

		UPDATE dbo.Purchase
		SET [SubTotal] = @SubTotal, [VAT] = @Vat, [FinalPrice] = @SubTotal + @Vat,
		    [ShippingName] = NULLIF(LTRIM(RTRIM(@ShippingName)), N''), [ShippingPrice] = @ShippingPrice,
		    [ShippingVat] = @ShippingVat
		WHERE [Id] = @Id;

		INSERT INTO dbo.PurchaseAddress([PurchaseId], [Name], [Line1], [Line2], [City], [Region],
		                                [PostCode], [Country], [Phone])
		SELECT @Id,
		       -- Who it is addressed to: the name the checkout gave, or the contact's own.
		       ISNULL(NULLIF(LTRIM(RTRIM(CONCAT(LTRIM(RTRIM(@FirstName)), N' ', LTRIM(RTRIM(@LastName))))), N''),
		              LTRIM(RTRIM(CONCAT(c.[FirstName], N' ', c.[LastName])))),
		       LTRIM(RTRIM(@Line1)), NULLIF(LTRIM(RTRIM(@Line2)), N''),
		       LTRIM(RTRIM(@City)), NULLIF(LTRIM(RTRIM(@Region)), N''),
		       NULLIF(LTRIM(RTRIM(@PostCode)), N''), UPPER(@Country),
		       ISNULL(NULLIF(LTRIM(RTRIM(@Phone)), N''), c.[Phone])
		FROM dbo.Contact c
		WHERE c.[Id] = @ContactId AND c.[AccountId] = @AccountId;

		-- Scoped to the site and to whoever was asking: a guest's cart has no contact, and
		-- @BasketId arrives from the caller.
		DELETE FROM dbo.Basket
		WHERE [Id] = @BasketId
		  AND [SiteId] = @SiteId
		  AND ((@SignedIn = 1 AND [ContactId] = @ContactId)
		       OR (@SignedIn = 0 AND [ContactId] IS NULL));

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		THROW;
	END CATCH

	SELECT @Reference AS [Reference], CAST(NULL AS nvarchar(max)) AS [ShortSkus];
END
