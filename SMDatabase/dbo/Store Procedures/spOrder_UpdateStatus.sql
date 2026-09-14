CREATE PROCEDURE [dbo].[spOrder_UpdateStatus]
	@Id int,
	@Status nvarchar(30),
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	UPDATE dbo.Purchase
	SET [Status] = @Status
	WHERE [Id] = @Id
	  AND [Reference] IS NOT NULL
	  AND [SiteId] = @SiteId;
END
