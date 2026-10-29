-- A store's delivery rates (T13), active or not: the portal lists both, and the storefront
-- filters to the active ones.
CREATE PROCEDURE [dbo].[spShippingRate_GetBySite]
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [Id], [Name], [Countries], [Price], [FreeOver], [IsActive]
	FROM dbo.ShippingRate
	WHERE [SiteId] = @SiteId
	ORDER BY [IsActive] DESC, [Price], [Name];
END
