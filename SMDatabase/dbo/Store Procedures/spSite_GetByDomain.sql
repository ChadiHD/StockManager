-- Host resolution for SMStore. Called once per request (cached), so it is a keyed single-row
-- read. Inactive sites are excluded rather than returned with a flag: a site that is switched
-- off should 404, not render half-configured.
CREATE PROCEDURE [dbo].[spSite_GetByDomain]
	@Domain nvarchar(253)
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [Id], [SiteKey], [Name], [Domain], [Country], [CurrencyCode], [Locale],
	       [OrderMode], [RegistrationFieldSet], [PriceDisplay], [MinMarginPct], [IsActive], [CreatedDate]
	FROM [dbo].[Site]
	WHERE [Domain] = @Domain
	  AND [IsActive] = 1;
END
