-- Staff profiles. Role membership lives in the Identity database (ApiAuthDb), so the API
-- joins these rows to AspNetUserRoles rather than doing it here.
CREATE PROCEDURE [dbo].[spUser_GetAll]
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [UserId], [FirstName], [LastName], [EmailAddress], [CreatedDate]
	FROM [dbo].[User]
	ORDER BY [FirstName], [LastName];
END
