/*
Sets a store's own wording for one message, from the admin email screen (T9). A NULL half keeps
the platform's subject or body, as EmailRenderer already reads it.

The wording is checked by the caller with EmailRenderer.WhyRefused before it gets here, so a
placeholder the message has no value for, or a required one left out, is refused when somebody
saves it — not discovered by the dispatcher at send time, which can only fall back to the
platform's words and log a warning nobody reads.
*/
CREATE PROCEDURE [dbo].[spSiteEmailTemplate_Save]
	@SiteId int,
	@TemplateKey nvarchar(80),
	@Subject nvarchar(200) = NULL,
	@Body nvarchar(max) = NULL
AS
BEGIN
	SET NOCOUNT ON;

	MERGE dbo.SiteEmailTemplate WITH (HOLDLOCK) AS t
	USING (SELECT @SiteId AS [SiteId], @TemplateKey AS [TemplateKey]) AS s
		ON t.[SiteId] = s.[SiteId] AND t.[TemplateKey] = s.[TemplateKey]
	WHEN MATCHED THEN
		UPDATE SET [Subject] = NULLIF(LTRIM(RTRIM(@Subject)), N''),
		           [Body] = NULLIF(LTRIM(RTRIM(@Body)), N''),
		           [UpdatedUtc] = SYSUTCDATETIME()
	WHEN NOT MATCHED THEN
		INSERT ([SiteId], [TemplateKey], [Subject], [Body])
		VALUES (@SiteId, @TemplateKey, NULLIF(LTRIM(RTRIM(@Subject)), N''), NULLIF(LTRIM(RTRIM(@Body)), N''));
END
