-- Contact carries no site of its own; it inherits the account's. Joining rather than trusting
-- the caller to have looked the account up first means a guessed account id returns an empty
-- list instead of another store's customer names and email addresses.
CREATE PROCEDURE [dbo].[spContact_GetByAccount]
	@AccountId int,
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [k].[Id], [k].[AccountId], [k].[IdentityUserId], [k].[FirstName], [k].[LastName],
	       [k].[Email], [k].[Phone], [k].[RoleInAccount], [k].[IsPrimary], [k].[Status],
	       [k].[CreatedDate]
	FROM [dbo].[Contact] k
	INNER JOIN [dbo].[Account] a
		ON a.[Id] = k.[AccountId]
		AND a.[SiteId] = @SiteId
	WHERE [k].[AccountId] = @AccountId
	ORDER BY [k].[IsPrimary] DESC, [k].[LastName], [k].[FirstName];
END
