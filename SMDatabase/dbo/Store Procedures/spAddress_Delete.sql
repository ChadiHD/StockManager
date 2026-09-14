-- Returns the row count so a caller can tell a delete from a no-op: an id belonging to
-- another store reports 0 rather than reading as a success.
CREATE PROCEDURE [dbo].[spAddress_Delete]
	@Id int,
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	DELETE [d]
	FROM dbo.Address d
	INNER JOIN dbo.Account a ON a.[Id] = d.[AccountId] AND a.[SiteId] = @SiteId
	WHERE [d].[Id] = @Id;

	SELECT @@ROWCOUNT AS [Deleted];
END
