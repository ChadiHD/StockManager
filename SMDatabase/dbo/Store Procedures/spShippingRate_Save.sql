/*
Adds a delivery rate to a store, or changes one of its own (T13). @Id NULL adds.

The site is in the predicate of the change, so a rate id from another store changes nothing and
reads as not found. Countries arrive normalised by the caller, ShippingRules: upper-case ISO
codes, comma-separated, or NULL for every country.

  50141  no such rate on this store
*/
CREATE PROCEDURE [dbo].[spShippingRate_Save]
	@SiteId int,
	@Id int = NULL,
	@Name nvarchar(100),
	@Countries nvarchar(400) = NULL,
	@Price money,
	@FreeOver money = NULL,
	@IsActive bit = 1
AS
BEGIN
	SET NOCOUNT ON;

	IF @Id IS NULL
	BEGIN
		INSERT INTO dbo.ShippingRate ([SiteId], [Name], [Countries], [Price], [FreeOver], [IsActive])
		VALUES (@SiteId, LTRIM(RTRIM(@Name)), NULLIF(@Countries, N''), @Price, @FreeOver, @IsActive);

		RETURN;
	END

	UPDATE dbo.ShippingRate
	SET [Name] = LTRIM(RTRIM(@Name)), [Countries] = NULLIF(@Countries, N''), [Price] = @Price,
	    [FreeOver] = @FreeOver, [IsActive] = @IsActive
	WHERE [Id] = @Id AND [SiteId] = @SiteId;

	IF @@ROWCOUNT = 0
	BEGIN
		THROW 50141, 'This store has no such delivery rate.', 1;
	END
END
