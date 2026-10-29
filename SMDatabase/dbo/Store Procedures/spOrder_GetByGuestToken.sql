/*
A guest's order, by the token in its /orders/{token} link (T12), and nothing else.

The token is the authorisation: the guest has no session and no login, and the reference beside
it is guessable. Hashed as spOrder_PlaceFromBasket hashed it, as nvarchar, and compared with the
stored hash. A token for another store's order reads as not found.

The columns spOrder_GetForAccount returns, so one model and one page serve both.
*/
CREATE PROCEDURE [dbo].[spOrder_GetByGuestToken]
	@Token nvarchar(64),
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [p].[Id], [p].[Reference], [p].[AccountId], [a].[Company] AS [AccountName],
	       [p].[Currency], [p].[Status], [p].[PurchaseDate], [p].[SubTotal], [p].[VAT], [p].[FinalPrice],
	       CAST(NULL AS nvarchar(20)) AS [FromQuoteReference],
	       [p].[PoNumber], [p].[TaxTreatment], [p].[TaxLegend],
	       [p].[ShippingName], [p].[ShippingPrice], [p].[TrackingNumber],
	       [a].[Email] AS [AccountEmail],
	       (SELECT COUNT(*) FROM [dbo].[PurchaseDetail] d WHERE d.[PurchaseId] = p.[Id]) AS [Items]
	FROM [dbo].[Purchase] p
	INNER JOIN [dbo].[Account] a ON a.[Id] = p.[AccountId] AND a.[SiteId] = @SiteId
	WHERE [p].[GuestTokenHash] = HASHBYTES('SHA2_256', @Token)
	  AND [p].[SiteId] = @SiteId;
END
