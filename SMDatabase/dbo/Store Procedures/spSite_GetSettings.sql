/*
One store's whole configuration, for the admin settings screen (T9), whether or not it is
active — an inactive store is exactly the one being configured. CurrencyLocked says whether
spSite_Update would refuse a new currency, so the screen can say so before anyone types one.
*/
CREATE PROCEDURE [dbo].[spSite_GetSettings]
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [Id], [SiteKey], [Name], [Domain], [Country], [CurrencyCode], [Locale],
	       [OrderMode], [RegistrationFieldSet], [PriceDisplay], [MinMarginPct],
	       [FeedStaleAfterHours], [HideStaleProducts], [OperatorEmail],
	       [TaxRuleSet], [StandardTaxRatePct], [TaxRegistrationNumber], [MailFromAddress],
	       [LegalName], [CompanyRegistrationNumber], [RegisteredAddress], [PricesIncludeTax], [FiscalIssuer],
	       [IsActive], [CreatedDate],
	       dbo.fnSite_CurrencyLocked([Id]) AS [CurrencyLocked],
	       -- Whether the store's payment keys are set (T13). Never the keys: they are write-only.
	       CAST(CASE WHEN [PaymentSecretRef] IS NOT NULL AND [PaymentWebhookSecretRef] IS NOT NULL
	                 THEN 1 ELSE 0 END AS bit) AS [PaymentConfigured]
	FROM [dbo].[Site]
	WHERE [Id] = @SiteId;
END
