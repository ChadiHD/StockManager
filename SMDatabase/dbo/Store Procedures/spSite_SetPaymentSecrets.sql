-- Stores a store's payment provider keys (T13), as ciphertext the caller produced. Both or
-- neither: a secret key with no webhook secret takes money and never hears that it did.
CREATE PROCEDURE [dbo].[spSite_SetPaymentSecrets]
	@SiteId int,
	@SecretRef nvarchar(max),
	@WebhookSecretRef nvarchar(max)
AS
BEGIN
	SET NOCOUNT ON;

	IF (@SecretRef IS NULL AND @WebhookSecretRef IS NOT NULL)
	   OR (@SecretRef IS NOT NULL AND @WebhookSecretRef IS NULL)
	BEGIN
		THROW 50140, 'A store needs both its secret key and its webhook signing secret, or neither.', 1;
	END

	UPDATE dbo.Site
	SET [PaymentSecretRef] = @SecretRef, [PaymentWebhookSecretRef] = @WebhookSecretRef
	WHERE [Id] = @SiteId;
END
