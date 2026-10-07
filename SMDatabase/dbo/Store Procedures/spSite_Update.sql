/*
Saves a store's settings from the admin screen (T9). Until then every column here was set by
updating the row by hand.

Everything but SiteKey and IsActive. The key names the store's theme folder and is in every
admin's stored selection, so it is fixed once created; activation has its own procedure, with a
checklist, because it is the one change that puts a store in front of customers.

Which OrderMode, RegistrationFieldSet and TaxRuleSet keys exist is decided in code, so the
caller checks those against SiteSettingKeys; PriceDisplay has CK_Site_PriceDisplay. What only
the data can answer is refused here:

  50090  the currency, once dbo.fnSite_CurrencyLocked says the store's prices are in it
  50091  a domain another store already answers on
  50092  a percentage outside 0-100, or a negative staleness threshold
  50093  no such store
*/
CREATE PROCEDURE [dbo].[spSite_Update]
	@SiteId int,
	@Name nvarchar(200),
	@Domain nvarchar(253),
	@Country nvarchar(2),
	@CurrencyCode nvarchar(3),
	@Locale nvarchar(10),
	@OrderMode nvarchar(20),
	@RegistrationFieldSet nvarchar(50),
	@PriceDisplay nvarchar(20),
	@MinMarginPct decimal(5, 2),
	@FeedStaleAfterHours int,
	@HideStaleProducts bit,
	@OperatorEmail nvarchar(320) = NULL,
	@TaxRuleSet nvarchar(50),
	@StandardTaxRatePct decimal(5, 2),
	@TaxRegistrationNumber nvarchar(30) = NULL,
	@MailFromAddress nvarchar(320) = NULL,
	@LegalName nvarchar(200) = NULL,
	@CompanyRegistrationNumber nvarchar(50) = NULL,
	@RegisteredAddress nvarchar(400) = NULL
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @CurrentCurrency nvarchar(3) = (SELECT [CurrencyCode] FROM dbo.Site WHERE [Id] = @SiteId);

	IF @CurrentCurrency IS NULL
	BEGIN
		THROW 50093, 'There is no such store.', 1;
	END

	IF @CurrencyCode <> @CurrentCurrency AND dbo.fnSite_CurrencyLocked(@SiteId) = 1
	BEGIN
		THROW 50090, 'The currency cannot change once the store has accounts, quotes or a feed: every price it holds is in the current one.', 1;
	END

	IF EXISTS (SELECT 1 FROM dbo.Site WHERE [Domain] = @Domain AND [Id] <> @SiteId)
	BEGIN
		THROW 50091, 'Another store already answers on that domain.', 1;
	END

	IF @MinMarginPct NOT BETWEEN 0 AND 100
	   OR @StandardTaxRatePct NOT BETWEEN 0 AND 100
	   OR @FeedStaleAfterHours < 0
	BEGIN
		THROW 50092, 'Percentages are between 0 and 100, and the staleness threshold is zero or more hours.', 1;
	END

	UPDATE dbo.Site
	SET [Name] = @Name,
	    [Domain] = @Domain,
	    [Country] = @Country,
	    [CurrencyCode] = @CurrencyCode,
	    [Locale] = @Locale,
	    [OrderMode] = @OrderMode,
	    [RegistrationFieldSet] = @RegistrationFieldSet,
	    [PriceDisplay] = @PriceDisplay,
	    [MinMarginPct] = @MinMarginPct,
	    [FeedStaleAfterHours] = @FeedStaleAfterHours,
	    [HideStaleProducts] = @HideStaleProducts,
	    [OperatorEmail] = NULLIF(LTRIM(RTRIM(@OperatorEmail)), N''),
	    [TaxRuleSet] = @TaxRuleSet,
	    [StandardTaxRatePct] = @StandardTaxRatePct,
	    [TaxRegistrationNumber] = NULLIF(LTRIM(RTRIM(@TaxRegistrationNumber)), N''),
	    [MailFromAddress] = NULLIF(LTRIM(RTRIM(@MailFromAddress)), N''),
	    [LegalName] = NULLIF(LTRIM(RTRIM(@LegalName)), N''),
	    [CompanyRegistrationNumber] = NULLIF(LTRIM(RTRIM(@CompanyRegistrationNumber)), N''),
	    [RegisteredAddress] = NULLIF(LTRIM(RTRIM(@RegisteredAddress)), N'')
	WHERE [Id] = @SiteId;
END
