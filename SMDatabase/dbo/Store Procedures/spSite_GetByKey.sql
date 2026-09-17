-- Lookup by the stable slug rather than the domain. Used where a site is named in
-- configuration or on a command line (seeding, the feed sync worker, admin tooling) and the
-- domain may not be known or may have changed.
CREATE PROCEDURE [dbo].[spSite_GetByKey]
	@SiteKey nvarchar(50)
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [Id], [SiteKey], [Name], [Domain], [Country], [CurrencyCode], [Locale],
	       [OrderMode], [RegistrationFieldSet], [PriceDisplay], [MinMarginPct],
	       [FeedStaleAfterHours], [HideStaleProducts], [OperatorEmail], [IsActive], [CreatedDate]
	FROM [dbo].[Site]
	-- Inactive sites are excluded here as they are in spSite_GetByDomain. Without it the two
	-- lookups disagree about what counts as a site, and a store taken offline still resolves
	-- through whichever path happens to use this one.
	WHERE [SiteKey] = @SiteKey
	  AND [IsActive] = 1;
END
