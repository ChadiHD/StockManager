-- Removes the feed definition. Products already imported from it are left in place; they keep
-- their Distributor value and simply stop being refreshed.
CREATE PROCEDURE [dbo].[spDistributorFeed_Delete]
	@Id int
AS
BEGIN
	SET NOCOUNT ON;

	DELETE FROM dbo.DistributorFeed
	WHERE [Id] = @Id;
END
