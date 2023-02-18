CREATE TABLE [dbo].[PurchaseDetail]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY, 
    [PurchaseId] INT NOT NULL, 
    [ProductId] INT NOT NULL, 
    [Quantity] INT NOT NULL DEFAULT 1,
    [PurchasePrice] MONEY NOT NULL, 
    [VAT] MONEY NOT NULL DEFAULT 0, 
)
