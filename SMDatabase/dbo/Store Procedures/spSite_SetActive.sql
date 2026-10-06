/*
Opens or closes a store (T9). Closing is immediate. Opening puts the store in front of
customers, so it is refused, naming everything still missing, until the store:

  - can send mail from its own address, and tell its operator when something fails;
  - charges tax — at a 0% standard rate a domestic customer is charged none — and prints the
    VAT number and legal name a customer needs on an invoice;
  - has written the pages @RequiredContent names (SiteContentKeys.RequiredToOpen: its terms of
    sale, privacy notice and cookie notice), passed in rather than repeated here so the list is
    one list;
  - has a category to sell anything under.

A store already open is not re-checked: this gates the change, and closing a live store because
a later edit blanked a field would be the wrong way round.

  50093  no such store
  50095  not ready to open, with what is missing
*/
CREATE PROCEDURE [dbo].[spSite_SetActive]
	@SiteId int,
	@IsActive bit,
	@RequiredContent nvarchar(400) = N'terms,privacy,cookies'
AS
BEGIN
	SET NOCOUNT ON;

	IF NOT EXISTS (SELECT 1 FROM dbo.Site WHERE [Id] = @SiteId)
	BEGIN
		THROW 50093, 'There is no such store.', 1;
	END

	IF @IsActive = 1 AND NOT EXISTS (SELECT 1 FROM dbo.Site WHERE [Id] = @SiteId AND [IsActive] = 1)
	BEGIN
		DECLARE @Missing TABLE ([Ordinal] int IDENTITY, [What] nvarchar(200));

		INSERT INTO @Missing ([What])
		SELECT checks.[What]
		FROM dbo.Site s
		CROSS APPLY (VALUES
			(1, CASE WHEN s.[MailFromAddress] IS NULL THEN N'an address to send mail from' END),
			(2, CASE WHEN s.[OperatorEmail] IS NULL THEN N'an operator address for alerts' END),
			-- "Zero", not "0%": a percent sign in a THROW message empties the whole message.
			(3, CASE WHEN s.[StandardTaxRatePct] <= 0 THEN N'a standard tax rate above zero' END),
			(4, CASE WHEN s.[TaxRegistrationNumber] IS NULL THEN N'its VAT number' END),
			(5, CASE WHEN s.[LegalName] IS NULL THEN N'its legal name' END),
			(6, CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.SiteCategory c WHERE c.[SiteId] = s.[Id] AND c.[IsActive] = 1)
			         THEN N'a category to sell under' END)
		) AS checks([Ordinal], [What])
		WHERE s.[Id] = @SiteId AND checks.[What] IS NOT NULL
		ORDER BY checks.[Ordinal];

		INSERT INTO @Missing ([What])
		SELECT N'its "' + LTRIM(RTRIM(required.[value])) + N'" page'
		FROM STRING_SPLIT(@RequiredContent, N',') required
		WHERE LTRIM(RTRIM(required.[value])) <> N''
		  AND NOT EXISTS (SELECT 1 FROM dbo.SiteContent sc
		                  WHERE sc.[SiteId] = @SiteId
		                    AND sc.[ContentKey] = LTRIM(RTRIM(required.[value]))
		                    AND sc.[BodyHtml] IS NOT NULL);

		IF EXISTS (SELECT 1 FROM @Missing)
		BEGIN
			DECLARE @Message nvarchar(2048) =
				N'This store cannot open yet. It still needs ' +
				(SELECT STRING_AGG([What], N', ') WITHIN GROUP (ORDER BY [Ordinal]) FROM @Missing) + N'.';

			THROW 50095, @Message, 1;
		END
	END

	UPDATE dbo.Site SET [IsActive] = @IsActive WHERE [Id] = @SiteId;
END
