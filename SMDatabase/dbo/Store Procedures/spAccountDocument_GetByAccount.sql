-- StoredName is deliberately absent. It is the key into the document store, and the only
-- thing that needs it is the download endpoint, which fetches one row by id after checking
-- who is asking. A list that carries it hands every caller the means to fetch every file.
CREATE PROCEDURE [dbo].[spAccountDocument_GetByAccount]
	@AccountId int,
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [f].[Id], [f].[AccountId], [f].[Kind], [f].[OriginalName], [f].[ContentType],
	       [f].[SizeBytes], [f].[UploadedByContactId], [f].[UploadedUtc], [f].[Status]
	FROM [dbo].[AccountDocument] f
	INNER JOIN [dbo].[Account] a
		ON a.[Id] = f.[AccountId]
		AND a.[SiteId] = @SiteId
	WHERE [f].[AccountId] = @AccountId
	ORDER BY [f].[UploadedUtc] DESC, [f].[Id];
END
