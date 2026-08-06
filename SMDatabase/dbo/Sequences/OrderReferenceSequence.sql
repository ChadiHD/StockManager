-- Supplies the numeric part of Purchase.Reference for sales orders (SO-3088).
CREATE SEQUENCE [dbo].[OrderReferenceSequence]
	AS INT
	START WITH 3001
	INCREMENT BY 1;
