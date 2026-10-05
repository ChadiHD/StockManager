/*
Records that a message is owed. The only writer of dbo.EmailOutbox.

Called two ways. From inside another procedure's transaction -- spAccount_Approve,
spAccount_Reject -- so the change and the message commit together. And from C# through
EmailOutbox, for the messages whose trigger has no procedure of its own: a registration spans
two databases, and a password reset is Identity's.

Returns no result set, deliberately. A procedure that calls this inside its own body and then
SELECTs its rowcount is read by LoadData, which takes the first result set it sees; a SELECT
here would arrive first and be read as the caller's answer.
*/
CREATE PROCEDURE [dbo].[spEmailOutbox_Enqueue]
	@SiteId int,
	@ToAddress nvarchar(256),
	@ToName nvarchar(200) = NULL,
	@TemplateKey nvarchar(80),
	@PayloadJson nvarchar(max),
	@PayloadProtected bit = 0
AS
BEGIN
	SET NOCOUNT ON;

	-- Refused rather than queued. A row with no address is a message that can only ever
	-- dead-letter, and the alert that follows would tell an operator about a problem the
	-- caller could have seen here.
	IF NULLIF(LTRIM(RTRIM(@ToAddress)), N'') IS NULL
	BEGIN
		THROW 50060, 'A message needs somebody to send it to.', 1;
	END

	INSERT INTO dbo.EmailOutbox([SiteId], [ToAddress], [ToName], [TemplateKey],
	                            [PayloadJson], [PayloadProtected])
	VALUES (@SiteId, LTRIM(RTRIM(@ToAddress)), NULLIF(LTRIM(RTRIM(@ToName)), N''), @TemplateKey,
	        @PayloadJson, @PayloadProtected);
END
