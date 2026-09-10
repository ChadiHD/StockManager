-- Lookup by the stable slug rather than the domain. Used where a site is named in
-- configuration or on a command line (seeding, the feed sync worker, admin tooling) and the
-- domain may not be known or may have changed.
CREATE PROCEDURE [dbo].[spSite_GetByKey]
	@SiteKey nvarchar(50)
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [Id], [SiteKey], [Name], [Domain], [Country], [CurrencyCode], [Locale],
	       [OrderMode], [RegistrationFieldSet], [PriceDisplay], [IsActive], [CreatedDate]
	FROM [dbo].[Site]
	WHERE [SiteKey] = @SiteKey;
END
