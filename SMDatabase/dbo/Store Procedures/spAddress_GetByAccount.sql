-- Gated through the account for the site, like every other child of Account.
CREATE PROCEDURE [dbo].[spAddress_GetByAccount]
	@AccountId int,
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [d].[Id], [d].[AccountId], [d].[Kind], [d].[Line1], [d].[Line2], [d].[City],
	       [d].[Region], [d].[PostCode], [d].[Country], [d].[IsDefault], [d].[CreatedDate]
	FROM [dbo].[Address] d
	INNER JOIN [dbo].[Account] a
		ON a.[Id] = d.[AccountId]
		AND a.[SiteId] = @SiteId
	WHERE [d].[AccountId] = @AccountId
	ORDER BY [d].[Kind], [d].[IsDefault] DESC, [d].[Id];
END
