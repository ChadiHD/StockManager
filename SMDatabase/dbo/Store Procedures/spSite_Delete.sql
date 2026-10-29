/*
Removes a store that never traded: one created by mistake, or a trial set up and abandoned.

Only a closed store with no accounts, quotes, orders, distributor feeds, payments, fiscal
documents or mail. Those are records a business keeps, and every one of them names the store,
so a store that has any is closed instead, which keeps them and lets nobody reach it. What goes
with the store is its own set-up: categories and their mappings, product placement, content
pages, email wording, delivery rates, customer groups, staff grants and anonymous baskets.

The last store is never removed. A database with no site renders nothing, and fnSite_Resolve
stops filling in a store for the inserts that rely on it.

The checks are not a lock. An account written in the moment between them and the delete fails
FK_Account_ToSite instead, which rolls everything back; SiteData reads that as the refusal.

  50093  no such store
  50097  not removable, with why
*/
CREATE PROCEDURE [dbo].[spSite_Delete]
	@SiteId int
AS
BEGIN
	SET NOCOUNT ON;

	IF NOT EXISTS (SELECT 1 FROM dbo.Site WHERE [Id] = @SiteId)
	BEGIN
		THROW 50093, 'There is no such store.', 1;
	END

	IF EXISTS (SELECT 1 FROM dbo.Site WHERE [Id] = @SiteId AND [IsActive] = 1)
	BEGIN
		THROW 50097, 'This store is open. Close it before removing it.', 1;
	END

	IF NOT EXISTS (SELECT 1 FROM dbo.Site WHERE [Id] <> @SiteId)
	BEGIN
		THROW 50097, 'This is the only store, and there must always be one.', 1;
	END

	DECLARE @Records TABLE ([Ordinal] int, [What] nvarchar(100));

	INSERT INTO @Records ([Ordinal], [What])
	SELECT held.[Ordinal], held.[What]
	FROM (VALUES
		(1, N'customer accounts', CASE WHEN EXISTS (SELECT 1 FROM dbo.Account WHERE [SiteId] = @SiteId) THEN 1 END),
		(2, N'quotes', CASE WHEN EXISTS (SELECT 1 FROM dbo.Quote WHERE [SiteId] = @SiteId) THEN 1 END),
		(3, N'orders', CASE WHEN EXISTS (SELECT 1 FROM dbo.Purchase WHERE [SiteId] = @SiteId) THEN 1 END),
		(4, N'distributor feeds', CASE WHEN EXISTS (SELECT 1 FROM dbo.DistributorFeed WHERE [SiteId] = @SiteId) THEN 1 END),
		(5, N'payments', CASE WHEN EXISTS (SELECT 1 FROM dbo.PaymentEvent WHERE [SiteId] = @SiteId) THEN 1 END),
		(6, N'fiscal documents', CASE WHEN EXISTS (SELECT 1 FROM dbo.FiscalDocument WHERE [SiteId] = @SiteId) THEN 1 END),
		(7, N'mail it has sent or is sending', CASE WHEN EXISTS (SELECT 1 FROM dbo.EmailOutbox WHERE [SiteId] = @SiteId) THEN 1 END)
	) AS held([Ordinal], [What], [Held])
	WHERE held.[Held] = 1;

	IF EXISTS (SELECT 1 FROM @Records)
	BEGIN
		DECLARE @Message nvarchar(2048) =
			N'This store cannot be removed, because it has ' +
			(SELECT STRING_AGG([What], N', ') WITHIN GROUP (ORDER BY [Ordinal]) FROM @Records) +
			N'. It can stay closed: nobody can reach a closed store, and its records are kept.';

		THROW 50097, @Message, 1;
	END

	-- Only from here: set at the top, a refusal above would roll back a caller's transaction
	-- along with nothing of this procedure's.
	SET XACT_ABORT ON;

	BEGIN TRY
		BEGIN TRANSACTION;

		DELETE v
		FROM dbo.GroupVisibility v
		INNER JOIN dbo.CustomerGroup g ON g.[Id] = v.[CustomerGroupId]
		WHERE g.[SiteId] = @SiteId;

		DELETE FROM dbo.CustomerGroup WHERE [SiteId] = @SiteId;

		-- Placement and mappings name the store's categories, so they go first.
		DELETE FROM dbo.SiteProduct WHERE [SiteId] = @SiteId;
		DELETE FROM dbo.CategoryMapping WHERE [SiteId] = @SiteId;
		DELETE FROM dbo.SiteCategory WHERE [SiteId] = @SiteId;

		DELETE FROM dbo.SiteContent WHERE [SiteId] = @SiteId;
		DELETE FROM dbo.SiteEmailTemplate WHERE [SiteId] = @SiteId;
		DELETE FROM dbo.ShippingRate WHERE [SiteId] = @SiteId;
		DELETE FROM dbo.UserSite WHERE [SiteId] = @SiteId;

		-- With no accounts there are no contacts, so these are anonymous visitors' baskets,
		-- left from when the store was open. BasketLine cascades.
		DELETE FROM dbo.Basket WHERE [SiteId] = @SiteId;

		DELETE FROM dbo.Site WHERE [Id] = @SiteId;

		COMMIT TRANSACTION;
	END TRY
	BEGIN CATCH
		IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
		THROW;
	END CATCH
END
