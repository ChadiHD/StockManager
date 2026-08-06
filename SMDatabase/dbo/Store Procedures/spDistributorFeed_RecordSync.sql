CREATE PROCEDURE [dbo].[spDistributorFeed_RecordSync]
	@Id int,
	@Status nvarchar(400)
AS
BEGIN
	SET NOCOUNT ON;

	UPDATE dbo.DistributorFeed
	SET [LastSyncedUtc] = SYSUTCDATETIME(),
	    [LastSyncStatus] = @Status
	WHERE [Id] = @Id;
END
