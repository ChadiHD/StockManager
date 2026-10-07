-- Goes back to the platform's wording for one message (T9). A store with no row is sent the
-- platform's words, never another store's.
CREATE PROCEDURE [dbo].[spSiteEmailTemplate_Delete]
	@SiteId int,
	@TemplateKey nvarchar(80)
AS
BEGIN
	SET NOCOUNT ON;

	DELETE FROM dbo.SiteEmailTemplate WHERE [SiteId] = @SiteId AND [TemplateKey] = @TemplateKey;
END
