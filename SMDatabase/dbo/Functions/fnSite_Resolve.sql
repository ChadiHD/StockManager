/*
The store a write belongs to, when the caller did not name one.

Every scoped entity carries a SiteId, but nothing in StockApi or SMPortal knows which store an
admin is acting for — there is no per-site admin identity yet, and inventing one here would be
guessing at a shape that belongs with customer identity. So the insert procedures let the
caller omit it, and a single-site database resolves unambiguously.

A second site makes that guess unsafe rather than merely imprecise, so this returns NULL and
the caller refuses the write instead of filing the row under whichever store happens to sort
first. That refusal is the point: it surfaces the missing decision at the moment it starts to
matter, which is the moment a second tenant exists.

Leaving SiteId NULL instead is not an option. UQ_CustomerGroup_Name, UQ_CustomerGroup_Slug and
UQ_DistributorFeed_Name are all scoped by site, and SQL Server treats NULL as a single distinct
value in a unique constraint — so with SiteId never populated, the second store to want a
"Reseller" group or a feed named "Main" collides with the first and cannot be onboarded at all.
*/
CREATE FUNCTION [dbo].[fnSite_Resolve] (@SiteId int)
RETURNS int
AS
BEGIN
	IF @SiteId IS NOT NULL
	BEGIN
		RETURN @SiteId;
	END

	DECLARE @Only int;

	-- HAVING without GROUP BY filters the single aggregate row, so this yields the site's id
	-- when there is exactly one and no row — leaving @Only NULL — when there is any other
	-- number of them.
	SELECT @Only = MIN([Id])
	FROM [dbo].[Site]
	HAVING COUNT(*) = 1;

	RETURN @Only;
END
