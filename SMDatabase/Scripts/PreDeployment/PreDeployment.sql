/*
Everything that has to run before the schema diff, in order.

A project has one pre-deployment script, so the steps are separate files included here. Each
says why it has to be pre-deployment; the common reason is that the diff is the first thing a
publish does, and some data has to be written or saved before it.
*/
:r .\BackfillPaymentTermsDays.sql
GO
:r .\HoldProductFlags.sql
GO
