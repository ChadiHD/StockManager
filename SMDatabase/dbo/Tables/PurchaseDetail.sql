CREATE TABLE [dbo].[PurchaseDetail]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY, 
    [PurchaseId] INT NOT NULL, 
    [ProductId] INT NOT NULL, 
    [Quantity] INT NOT NULL DEFAULT 1,
    [PurchasePrice] MONEY NOT NULL, 
    [VAT] MONEY NOT NULL DEFAULT 0, 
    CONSTRAINT [FK_PurchaseDetail_ToPurchase] FOREIGN KEY (PurchaseId) REFERENCES Purchase(Id), 
    CONSTRAINT [FK_PurchaseDetail_ToProduct] FOREIGN KEY (ProductId) REFERENCES Product(Id), 
)
