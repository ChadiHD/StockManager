CREATE PROCEDURE [dbo].[spUser_Insert]
	@UserId nvarchar(128),
	@FirstName nvarchar(50),
	@LastName nvarchar(50),
	@EmailAddress nvarchar(256),
	-- Only AdminBootstrap passes 1: the first admin of a deployment has to be able to give
	-- everybody else their stores.
	@AllSites bit = 0

AS
begin
	set NOCOUNT on;

	INSERT INTO dbo.[User] ( UserId, FirstName, LastName, EmailAddress, AllSites)
	VALUES( @UserId, @FirstName, @LastName, @EmailAddress, @AllSites)
end