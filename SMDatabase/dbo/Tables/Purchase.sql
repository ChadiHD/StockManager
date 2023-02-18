CREATE TABLE [dbo].[Purchase]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY, 
    [StaffId] NVARCHAR(128) NOT NULL, 
    [PurchaseDate] DATETIME2 NOT NULL , 
    [SubTotal] MONEY NOT NULL, 
    [VAT] MONEY NOT NULL, 
    [FinalPrice] MONEY NOT NULL
)
