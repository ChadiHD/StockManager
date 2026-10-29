-- A store's payment provider keys, as ciphertext (T13). The storefront's only read of them: no
-- settings read projects them, so no screen can hand them back.
CREATE PROCEDURE [dbo].[spSite_GetPaymentSecrets]
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [PaymentSecretRef] AS [SecretRef], [PaymentWebhookSecretRef] AS [WebhookSecretRef]
	FROM dbo.Site
	WHERE [Id] = @SiteId;
END
