CREATE PROCEDURE [dbo].[spUser_Insert]
	@UserId nvarchar(128),
	@FirstName nvarchar(50),
	@LastName nvarchar(50),
	@EmailAddress nvarchar(256)

AS
begin
	set NOCOUNT on;

	INSERT INTO dbo.[User] ( UserId, FirstName, LastName, EmailAddress)
	VALUES( @UserId, @FirstName, @LastName, @EmailAddress)
end