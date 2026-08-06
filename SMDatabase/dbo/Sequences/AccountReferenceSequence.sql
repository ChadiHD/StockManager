-- Supplies the numeric part of Account.Reference (AC-2041). A sequence keeps references
-- stable and gap-tolerant without reading MAX() under concurrency.
CREATE SEQUENCE [dbo].[AccountReferenceSequence]
	AS INT
	START WITH 2001
	INCREMENT BY 1;
