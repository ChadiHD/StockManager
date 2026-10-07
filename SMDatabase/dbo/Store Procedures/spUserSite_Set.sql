/*
Sets which stores a member of staff may act for: every store, or exactly the ones listed.

Replaces rather than adds, so the screen's checkboxes are the whole answer and a store unticked
is a store taken away. A user who may act for every store keeps no rows — the flag already says
it, and rows left behind would quietly come back if the flag were ever turned off. Ids that name
no store are ignored rather than refused: the list comes from a screen that only offers real
ones, and a store deleted under it is not this caller's mistake.

Whether turning AllSites off leaves the deployment with no admin who can manage every store is
checked by the caller, because roles live in ApiAuthDb and this database cannot see them.
*/
CREATE PROCEDURE [dbo].[spUserSite_Set]
	@UserId nvarchar(128),
	@AllSites bit,
	@SiteIds [dbo].[SiteIdList] READONLY
AS
BEGIN
	SET NOCOUNT ON;

	IF NOT EXISTS (SELECT 1 FROM dbo.[User] WHERE [UserId] = @UserId)
	BEGIN
		THROW 50080, 'There is no member of staff with that id.', 1;
	END

	BEGIN TRY
		BEGIN TRANSACTION;

		UPDATE dbo.[User] SET [AllSites] = @AllSites WHERE [UserId] = @UserId;

		DELETE us
		FROM dbo.UserSite us
		WHERE us.[UserId] = @UserId
		  AND (@AllSites = 1 OR NOT EXISTS (SELECT 1 FROM @SiteIds s WHERE s.[SiteId] = us.[SiteId]));

		IF @AllSites = 0
		BEGIN
			INSERT INTO dbo.UserSite ([UserId], [SiteId])
			SELECT @UserId, s.[Id]
			FROM @SiteIds ids
			INNER JOIN dbo.Site s ON s.[Id] = ids.[SiteId]
			WHERE NOT EXISTS (SELECT 1 FROM dbo.UserSite us
			                  WHERE us.[UserId] = @UserId AND us.[SiteId] = s.[Id]);
		END

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		THROW;
	END CATCH
END
