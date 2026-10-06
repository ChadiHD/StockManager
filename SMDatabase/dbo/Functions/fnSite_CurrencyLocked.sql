/*
Whether a store's currency can no longer be changed: 1 once it has an account, a quote or a
feed (T9).

Every price a store holds is a bare number in its currency — an account's credit limit, a
quote's lines, a feed's whole catalog — and changing the code would relabel all of them rather
than convert any. Before any of those exist the code is still a setting; after, it is a fact
about the data. One function, so the settings screen that greys the field out and spSite_Update
that refuses the change cannot disagree.
*/
CREATE FUNCTION [dbo].[fnSite_CurrencyLocked]
(
	@SiteId int
)
RETURNS bit
AS
BEGIN
	RETURN CASE
		WHEN EXISTS (SELECT 1 FROM dbo.Account WHERE [SiteId] = @SiteId) THEN 1
		WHEN EXISTS (SELECT 1 FROM dbo.Quote WHERE [SiteId] = @SiteId) THEN 1
		WHEN EXISTS (SELECT 1 FROM dbo.DistributorFeed WHERE [SiteId] = @SiteId) THEN 1
		ELSE 0
	END;
END
