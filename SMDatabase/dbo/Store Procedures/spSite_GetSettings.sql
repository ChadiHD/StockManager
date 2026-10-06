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
	       [LegalName], [CompanyRegistrationNumber], [RegisteredAddress],
	       [IsActive], [CreatedDate],
	       dbo.fnSite_CurrencyLocked([Id]) AS [CurrencyLocked]
	FROM [dbo].[Site]
	WHERE [Id] = @SiteId;
END
