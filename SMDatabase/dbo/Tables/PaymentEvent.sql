/*
Every payment provider event a store's webhook has applied (T13), once.

Keyed on the provider's own event id, and written in the transaction that applies the event, so
a replay or a second delivery of the same event finds its row and changes nothing. Providers
deliver at least once, and retry anything that did not answer 2xx in time.
*/
CREATE TABLE [dbo].[PaymentEvent]
(
	[EventId] NVARCHAR(100) NOT NULL PRIMARY KEY,
	[SiteId] INT NOT NULL,
	[EventType] NVARCHAR(100) NOT NULL,
	[Reference] NVARCHAR(20) NULL,
	[ReceivedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_PaymentEvent_ReceivedUtc] DEFAULT SYSUTCDATETIME(),

	CONSTRAINT [FK_PaymentEvent_ToSite] FOREIGN KEY ([SiteId]) REFERENCES [Site]([Id])
)
