-- One product and a quantity. No price columns, on purpose: a basket line says what the
-- customer wants, and the price is resolved when the request is submitted. A price stored here
-- would be a price the customer keeps while the catalog moves under it, and reconciling the
-- two at submit is work with no right answer.
CREATE TABLE [dbo].[BasketLine]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[BasketId] INT NOT NULL,
	[ProductId] INT NOT NULL,
	[Quantity] INT NOT NULL DEFAULT 1,
	[AddedUtc] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),

	-- Cascades because a basket is the only thing that gives its lines meaning; an orphaned
	-- line is not history worth keeping, unlike a quote line.
	CONSTRAINT [FK_BasketLine_ToBasket] FOREIGN KEY ([BasketId])
		REFERENCES [Basket]([Id]) ON DELETE CASCADE,
	CONSTRAINT [FK_BasketLine_ToProduct] FOREIGN KEY ([ProductId]) REFERENCES [Product]([Id]),

	-- Adding a product already in the basket raises its quantity rather than making a second
	-- line, and this is what makes spBasket_AddLine's upsert expressible.
	CONSTRAINT [UQ_BasketLine_Product] UNIQUE ([BasketId], [ProductId]),

	-- A zero or negative quantity is a removal, and spBasket_SetQuantity treats it as one. A
	-- line that reached the table with one would multiply into a negative line total.
	CONSTRAINT [CK_BasketLine_Quantity] CHECK ([Quantity] > 0)
)
