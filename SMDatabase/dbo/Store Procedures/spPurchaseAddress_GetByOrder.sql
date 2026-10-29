-- Where an order goes (T12). Through the order, so the site is in the predicate: a caller that
-- found the order for the wrong store finds no address either.
CREATE PROCEDURE [dbo].[spPurchaseAddress_GetByOrder]
	@PurchaseId int,
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [pa].[Name], [pa].[Line1], [pa].[Line2], [pa].[City], [pa].[Region],
	       [pa].[PostCode], [pa].[Country], [pa].[Phone]
	FROM [dbo].[PurchaseAddress] pa
	INNER JOIN [dbo].[Purchase] p ON p.[Id] = pa.[PurchaseId]
	WHERE [pa].[PurchaseId] = @PurchaseId
	  AND [p].[SiteId] = @SiteId;
END
