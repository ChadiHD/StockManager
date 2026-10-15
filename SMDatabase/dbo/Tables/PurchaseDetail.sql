CREATE TABLE [dbo].[PurchaseDetail]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY, 
    [PurchaseId] INT NOT NULL, 
    [ProductId] INT NOT NULL, 
    [Quantity] INT NOT NULL DEFAULT 1,
    -- A portal order's unit price on its store's basis (T10): net on a trade store, with tax in on
    -- a store whose Site.PricesIncludeTax is set, where VAT is the part of it that is tax. A
    -- desktop POS sale writes the line total here instead.
    [PurchasePrice] MONEY NOT NULL,
    [VAT] MONEY NOT NULL DEFAULT 0,

    -- The rate that produced the VAT beside it. Per line rather than per order, because
    -- Product.IsTaxable varies by product and a mixed basket carries two rates on one
    -- document -- and because reading a money figure without the rate that made it leaves
    -- anybody checking the arithmetic to guess.
    [TaxRatePct] DECIMAL(5, 2) NOT NULL DEFAULT 0, 
    CONSTRAINT [FK_PurchaseDetail_ToPurchase] FOREIGN KEY (PurchaseId) REFERENCES Purchase(Id), 
    CONSTRAINT [FK_PurchaseDetail_ToProduct] FOREIGN KEY (ProductId) REFERENCES Product(Id), 
)
