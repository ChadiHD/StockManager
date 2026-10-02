-- A store's own wording for one message, or nothing -- in which case the platform's applies.
-- Scoped by site, so one store's wording can never be read for another's customer.
CREATE PROCEDURE [dbo].[spSiteEmailTemplate_Get]
	@SiteId int,
	@TemplateKey nvarchar(80)
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [SiteId], [TemplateKey], [Subject], [Body]
	FROM dbo.SiteEmailTemplate
	WHERE [SiteId] = @SiteId
	  AND [TemplateKey] = @TemplateKey;
END
