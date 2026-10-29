/*
Records the checkout session now paying for a cart order (T13), and returns the one it replaces
so the caller can expire it. Only while the order still awaits payment: a paid or cancelled
order takes no new session.

The previous session is expired by the caller, at the provider, so a customer who started paying
twice can finish only once. Its expiry event then names a session that is no longer the order's,
and spOrder_ApplyPaymentEvent leaves the order alone.
*/
CREATE PROCEDURE [dbo].[spOrder_SetPaymentSession]
	@SiteId int,
	@Reference nvarchar(20),
	@SessionId nvarchar(200)
AS
BEGIN
	SET NOCOUNT ON;

	UPDATE dbo.Purchase
	SET [PaymentSessionId] = @SessionId
	OUTPUT deleted.[PaymentSessionId] AS [PreviousSessionId]
	WHERE [Reference] = @Reference
	  AND [SiteId] = @SiteId
	  AND [QuoteId] IS NULL
	  AND [Status] = N'Awaiting payment';
END
