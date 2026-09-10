-- Every site, active or not. Feeds the admin portal's site picker and the feed sync worker,
-- which loops over active sites; callers that only want live stores filter on IsActive.
CREATE PROCEDURE [dbo].[spSite_GetAll]
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [Id], [SiteKey], [Name], [Domain], [Country], [CurrencyCode], [Locale],
	       [OrderMode], [RegistrationFieldSet], [PriceDisplay], [IsActive], [CreatedDate]
	FROM [dbo].[Site]
	ORDER BY [Name];
END
