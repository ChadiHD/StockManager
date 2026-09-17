-- Accept or reject one supporting document during account review, so a single bad scan does
-- not have to fail the whole application.
CREATE PROCEDURE [dbo].[spAccountDocument_SetStatus]
	@Id int,
	@Status nvarchar(20),
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	UPDATE [f]
	SET [f].[Status] = @Status
	FROM dbo.AccountDocument f
	INNER JOIN dbo.Account a ON a.[Id] = f.[AccountId] AND a.[SiteId] = @SiteId
	WHERE [f].[Id] = @Id;
END
