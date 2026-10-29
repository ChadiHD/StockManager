/*
What a store charges for delivery (T13): a flat price per rate, for the countries it names.

The cheapest active rate covering the delivery country is charged, by ShippingQuote.Choose.
Countries is a comma-separated list of ISO 3166-1 alpha-2 codes, NULL meaning every country: a
store's rates are a handful, and a child table for them would be a join on every checkout for
nothing. Price is on the store's basis, with tax in on an inclusive store, like a product's.
FreeOver waives it when the goods come to that much or more, on the same basis.

Deactivated rather than deleted, like a category: an order names the rate it was charged.
*/
CREATE TABLE [dbo].[ShippingRate]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[SiteId] INT NOT NULL,
	[Name] NVARCHAR(100) NOT NULL,
	[Countries] NVARCHAR(400) NULL,
	[Price] MONEY NOT NULL,
	[FreeOver] MONEY NULL,
	[IsActive] BIT NOT NULL CONSTRAINT [DF_ShippingRate_IsActive] DEFAULT 1,

	CONSTRAINT [FK_ShippingRate_ToSite] FOREIGN KEY ([SiteId]) REFERENCES [Site]([Id]),
	CONSTRAINT [CK_ShippingRate_Price] CHECK ([Price]>=(0))
)
