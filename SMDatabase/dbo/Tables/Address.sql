-- Billing and delivery addresses for a trading account.
--
-- Held as rows rather than as columns on Account because a B2B customer routinely has one
-- invoicing address and several delivery sites, and because the billing address is what the
-- tax treatment is decided from — an order has to record which address it was placed against,
-- not just which company placed it.
CREATE TABLE [dbo].[Address]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[AccountId] INT NOT NULL,

	-- 'Billing' | 'Shipping'
	[Kind] NVARCHAR(20) NOT NULL,

	[Line1] NVARCHAR(200) NOT NULL,
	[Line2] NVARCHAR(200) NULL,
	[City] NVARCHAR(100) NOT NULL,

	-- County, state or province. Optional because plenty of addressing systems have no such
	-- level, and demanding one is how a form becomes unfillable in another country.
	[Region] NVARCHAR(100) NULL,

	-- Nullable for the same reason: Ireland had no general postcode system until Eircode and
	-- adoption is still partial, which is exactly the kind of assumption a platform meant for
	-- several countries should not bake in.
	[PostCode] NVARCHAR(20) NULL,

	-- ISO 3166-1 alpha-2, matching Site.Country, because tax treatment is decided by
	-- comparing the two and a free-text country name cannot be compared.
	[Country] NVARCHAR(2) NOT NULL,

	-- The one offered first for its kind. UQ_Address_OneDefault keeps it at one per kind.
	[IsDefault] BIT NOT NULL DEFAULT 0,

	[CreatedDate] DATETIME2 NOT NULL DEFAULT getutcdate(),

	CONSTRAINT [FK_Address_ToAccount] FOREIGN KEY ([AccountId]) REFERENCES [Account]([Id]),
	CONSTRAINT [CK_Address_Kind] CHECK ([Kind] IN ('Billing', 'Shipping'))
)
GO

-- One default per kind per account: an account may have a default billing address and a
-- default shipping address, but not two of either.
CREATE UNIQUE NONCLUSTERED INDEX [UQ_Address_OneDefault]
	ON [dbo].[Address] ([AccountId], [Kind])
	WHERE [IsDefault] = 1;
