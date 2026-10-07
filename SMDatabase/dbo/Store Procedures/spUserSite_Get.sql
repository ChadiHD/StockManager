/*
The stores staff have been given, one row per user and store: one user's when @UserId is
given, everybody's for the users screen when it is not. A user who may act for every store has
no rows here; that is dbo.[User].AllSites.
*/
CREATE PROCEDURE [dbo].[spUserSite_Get]
	@UserId nvarchar(128) = NULL
AS
BEGIN
	SET NOCOUNT ON;

	SELECT us.[UserId], us.[SiteId]
	FROM dbo.UserSite us
	WHERE @UserId IS NULL OR us.[UserId] = @UserId
	ORDER BY us.[UserId], us.[SiteId];
END
