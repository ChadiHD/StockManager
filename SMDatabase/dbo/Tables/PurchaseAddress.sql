/*
Where an order goes, as it was given at checkout (T12).

A copy, not a reference to dbo.Address. An account's address book can change after the order,
and a guest has no address book at all, so the order keeps its own. One row per order: the
delivery address. A card's billing address is the payment provider's to collect.

The shape is dbo.Address's, region and postcode optional for the reason given there.
*/
CREATE TABLE [dbo].[PurchaseAddress]
(
	[PurchaseId] INT NOT NULL PRIMARY KEY,
	[Name] NVARCHAR(200) NOT NULL,
	[Line1] NVARCHAR(200) NOT NULL,
	[Line2] NVARCHAR(200) NULL,
	[City] NVARCHAR(100) NOT NULL,
	[Region] NVARCHAR(100) NULL,
	[PostCode] NVARCHAR(20) NULL,
	-- ISO 3166-1 alpha-2, as on dbo.Address: the tax was assessed on it.
	[Country] NVARCHAR(2) NOT NULL,
	[Phone] NVARCHAR(50) NULL,

	CONSTRAINT [FK_PurchaseAddress_ToPurchase] FOREIGN KEY ([PurchaseId]) REFERENCES [Purchase]([Id])
)
