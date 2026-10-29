/*
Records that an order owes a fiscal document (T14), when its store issues them. The only writer of
dbo.FiscalDocument rows.

Called from inside the transaction that makes the order owe one: spOrder_ApplyPaymentEvent when a
payment marks it Paid, and spOrder_UpdateStatus when an admin marks it Paid, or Fulfilled without
anyone marking it paid. A store on 'none' queues nothing, and an order that already has a row
gets no second, so calling it twice for one sale is harmless.

The store and the issuer come from the order, never from a caller. Returns no result set, for
spEmailOutbox_Enqueue's reason: its callers SELECT their own answer.
*/
CREATE PROCEDURE [dbo].[spFiscalDocument_Enqueue]
	@PurchaseId int
AS
BEGIN
	SET NOCOUNT ON;

	INSERT INTO dbo.FiscalDocument ([SiteId], [PurchaseId], [Issuer])
	SELECT p.[SiteId], p.[Id], s.[FiscalIssuer]
	FROM dbo.Purchase p
	INNER JOIN dbo.Site s ON s.[Id] = p.[SiteId]
	WHERE p.[Id] = @PurchaseId
	  AND p.[Reference] IS NOT NULL
	  AND s.[FiscalIssuer] <> N'none'
	  -- UPDLOCK and HOLDLOCK, so a webhook and an admin marking one order paid together queue here
	  -- rather than both finding nothing and the second failing UQ_FiscalDocument_PurchaseId,
	  -- which would roll back the payment it was called from.
	  AND NOT EXISTS (SELECT 1 FROM dbo.FiscalDocument f WITH (UPDLOCK, HOLDLOCK)
	                  WHERE f.[PurchaseId] = p.[Id]);
END
