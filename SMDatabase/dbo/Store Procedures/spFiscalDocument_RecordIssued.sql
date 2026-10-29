/*
Records the document an issuer returned (T14), only while this claim still holds the row, for
spEmailOutbox_RecordSent's reason. Selects 1 when recorded.
*/
CREATE PROCEDURE [dbo].[spFiscalDocument_RecordIssued]
	@Id int,
	@ClaimToken uniqueidentifier,
	@Number nvarchar(100),
	@Url nvarchar(400) = NULL
AS
BEGIN
	SET NOCOUNT ON;

	UPDATE dbo.FiscalDocument
	SET [Status] = N'Issued',
	    [Number] = @Number,
	    [Url] = NULLIF(LTRIM(RTRIM(@Url)), N''),
	    [IssuedUtc] = SYSUTCDATETIME(),
	    [LastError] = NULL
	WHERE [Id] = @Id
	  AND [ClaimToken] = @ClaimToken
	  AND [Status] = N'Issuing';

	SELECT @@ROWCOUNT AS [Recorded];
END
