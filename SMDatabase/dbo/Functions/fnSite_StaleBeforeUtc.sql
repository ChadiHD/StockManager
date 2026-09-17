-- The cutoff before which a distributor-sourced product counts as stale for this store, or NULL
-- when the store does not hide stale products at all.
--
-- Exists so the three catalog procedures resolve it the same way. They each have to pass the
-- cutoff into dbo.fnCatalog_VisibleProducts, which is inline and therefore cannot DECLARE
-- anything of its own, and three hand-written copies of "is hiding on, and how many hours" is
-- the kind of triplication that drifts — the facet counts disagreeing with the page they filter
-- to is exactly the bug fnCatalog_VisibleProducts was extracted to kill.
--
-- **Assign it to a variable. Never call it per row.** A scalar function evaluated inside a
-- WHERE or a SELECT list runs once per row and defeats the plan; assigned to a DECLARE it runs
-- once, which is the only way it is used here.
--
-- NULL means "no hiding", and covers both switches: hiding turned off, and a threshold of zero.
-- Returning a very old date instead would look equivalent and would not be — the callers use
-- NULL to skip the predicate entirely rather than to pass one that matches everything.
CREATE FUNCTION [dbo].[fnSite_StaleBeforeUtc]
(
	@SiteId int
)
RETURNS datetime2
AS
BEGIN
	DECLARE @Cutoff datetime2;

	SELECT @Cutoff = CASE
		WHEN [HideStaleProducts] = 1 AND [FeedStaleAfterHours] > 0
		THEN DATEADD(HOUR, -[FeedStaleAfterHours], SYSUTCDATETIME())
	END
	FROM dbo.Site
	WHERE [Id] = @SiteId;

	RETURN @Cutoff;
END
