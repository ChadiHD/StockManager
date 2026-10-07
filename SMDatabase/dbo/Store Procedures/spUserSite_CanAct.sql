/*
Whether a member of staff may act for a store in the admin portal: 1 or 0.

Called by AdminSiteResolutionMiddleware on every API request that names a store, so it is two
primary-key reads and nothing else. A user with no profile row may act for nothing.
*/
CREATE PROCEDURE [dbo].[spUserSite_CanAct]
	@UserId nvarchar(128),
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT CAST(CASE
		WHEN EXISTS (SELECT 1 FROM dbo.[User] WHERE [UserId] = @UserId AND [AllSites] = 1) THEN 1
		WHEN EXISTS (SELECT 1 FROM dbo.UserSite WHERE [UserId] = @UserId AND [SiteId] = @SiteId) THEN 1
		ELSE 0
	END AS bit);
END
