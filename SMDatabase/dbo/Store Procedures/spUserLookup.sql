CREATE PROCEDURE [dbo].[spUserLookup]
	@Id nvarchar(128)

AS
begin
	set NOCOUNT on;

	SELECT UserId, FirstName, LastName, EmailAddress, CreatedDate
	FROM [dbo].[User]
	WHERE UserId = @Id;
end