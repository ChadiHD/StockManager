/*
Creates a store, closed (T9). Until then a store was a row inserted by hand, live the moment it
existed. A new store is configured from the portal — settings, categories, content, wording —
while nobody can reach it, and opened by spSite_SetActive once its checklist is met.

Ordering and price display take the table's defaults; the settings screen changes them. The
caller has checked the keys against SiteSettingKeys and the key's shape.

  50094  a key another store already has: it names theme files and is in admins' stored choice
  50091  a domain another store already answers on
*/
CREATE PROCEDURE [dbo].[spSite_Insert]
	@SiteKey nvarchar(50),
	@Name nvarchar(200),
	@Domain nvarchar(253),
	@Country nvarchar(2),
	@CurrencyCode nvarchar(3),
	@Locale nvarchar(10),
	@RegistrationFieldSet nvarchar(50),
	@TaxRuleSet nvarchar(50)
AS
BEGIN
	SET NOCOUNT ON;

	IF EXISTS (SELECT 1 FROM dbo.Site WHERE [SiteKey] = @SiteKey)
	BEGIN
		THROW 50094, 'Another store already has that key.', 1;
	END

	IF EXISTS (SELECT 1 FROM dbo.Site WHERE [Domain] = @Domain)
	BEGIN
		THROW 50091, 'Another store already answers on that domain.', 1;
	END

	INSERT INTO dbo.Site ([SiteKey], [Name], [Domain], [Country], [CurrencyCode], [Locale],
	                      [RegistrationFieldSet], [TaxRuleSet], [IsActive])
	VALUES (@SiteKey, LTRIM(RTRIM(@Name)), @Domain, @Country, @CurrencyCode, @Locale,
	        @RegistrationFieldSet, @TaxRuleSet, 0);

	SELECT CAST(SCOPE_IDENTITY() AS int);
END
