/*
One document, with the stored name, for the endpoint that streams it back.

This is the authorisation check for a customer-uploaded file, and it is a query rather than a
code path on purpose: the site predicate and the row lookup happen together, so there is no
window in which a caller holds a row it has not been cleared for. An id from another store
returns nothing, and the endpoint renders that as 404 — not 403, which would confirm the
document exists.

The caller still has to check that the account is one this requester may see. A store admin
may see any of their store's; a contact may see only their own account's.
*/
CREATE PROCEDURE [dbo].[spAccountDocument_GetById]
	@Id int,
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	SELECT [f].[Id], [f].[AccountId], [f].[Kind], [f].[StoredName], [f].[OriginalName],
	       [f].[ContentType], [f].[SizeBytes], [f].[UploadedByContactId], [f].[UploadedUtc],
	       [f].[Status]
	FROM [dbo].[AccountDocument] f
	INNER JOIN [dbo].[Account] a
		ON a.[Id] = f.[AccountId]
		AND a.[SiteId] = @SiteId
	WHERE [f].[Id] = @Id;
END
