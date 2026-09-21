/*
Gives every existing account a term in days before CK_Account_Terms starts demanding one.

This has to be pre-deployment, and the reason is worth stating because the failure it avoids
happens on a tenant's database rather than here. A DACPAC adds Account.PaymentTermsDays with
its default of 0 and then creates CK_Account_Terms, which says days and label must agree —
and every existing "Net 30" account fails it. The publish stops at that point, having already
added the column, leaving the schema half-applied. A column default cannot read another
column, so the value has to be written before the constraint exists, and the schema diff is
the first thing a publish does. Pre-deployment is the only phase in front of it.

The development database happens to hold nothing but Prepaid accounts, so this would pass
unnoticed today. That is luck, not correctness: the first store with a credit customer is the
one that finds out.

Idempotent, like the post-deployment seed, because it runs on every publish.
*/

IF COL_LENGTH('dbo.Account', 'PaymentTermsDays') IS NULL
BEGIN
    -- Named to match the table definition. Left unnamed, SQL Server invents a name and the
    -- next publish sees a default constraint that does not match the model and recreates it.
    ALTER TABLE dbo.Account
        ADD [PaymentTermsDays] INT NOT NULL
            CONSTRAINT [DF_Account_PaymentTermsDays] DEFAULT 0;
END
GO

/*
"Net 30" carries its own answer; anything else that is not Prepaid gets thirty days.

Falling back to zero would be worse than wrong: zero means prepaid, so an account whose terms
nobody can parse would quietly move onto immediate payment and its customer would be refused
at acceptance for a credit facility they have.
*/
UPDATE dbo.Account
SET [PaymentTermsDays] =
        CASE
            WHEN [PaymentTerms] = N'Prepaid' THEN 0
            WHEN TRY_CAST(REPLACE(UPPER([PaymentTerms]), N'NET', N'') AS int) > 0
                THEN TRY_CAST(REPLACE(UPPER([PaymentTerms]), N'NET', N'') AS int)
            ELSE 30
        END
WHERE ([PaymentTerms] = N'Prepaid' AND [PaymentTermsDays] <> 0)
   OR ([PaymentTerms] <> N'Prepaid' AND [PaymentTermsDays] = 0);
GO
