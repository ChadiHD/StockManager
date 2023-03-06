﻿CREATE PROCEDURE [dbo].[spProduct_GetById]
	@Id int

AS
BEGIN
	SET NOCOUNT ON;

	SELECT [Id], [ProductName], [Description], [RetailPrice], [IsTaxable]
	FROM dbo.Product
	WHERE Id = @Id
END
