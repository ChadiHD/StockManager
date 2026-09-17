-- A person who signs in on behalf of a trading account.
--
-- Distinct from dbo.[User], which holds staff — the people who run the store. A Contact is a
-- customer's employee, and several of them share one Account: the buyer who raises a quote
-- and the finance contact who approves it are two rows against the same company.
--
-- This table is also how an authenticated request reaches a site. ASP.NET Identity has no
-- concept of a store, so the chain is Identity user -> Contact -> Account -> Site, and the
-- last link is what a sign-in has to check before it will serve anything.
CREATE TABLE [dbo].[Contact]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[AccountId] INT NOT NULL,

	/*
	The AspNetUsers.Id this contact signs in as.

	No foreign key, and there cannot be one: Identity lives in ApiAuthDb and this table lives
	in SMDatabase. Nothing at the database level keeps the two in step, so
	spAccount_Register creates both inside one transaction and the registration path is the
	only thing allowed to write this column.

	Nullable because a contact can exist without a login — a finance address on file that
	never signs in is still a contact — and because the admin portal can add one before an
	invitation is accepted.
	*/
	[IdentityUserId] NVARCHAR(450) NULL,

	[FirstName] NVARCHAR(100) NOT NULL,
	[LastName] NVARCHAR(100) NOT NULL,
	[Email] NVARCHAR(256) NOT NULL,
	[Phone] NVARCHAR(50) NULL,

	-- 'Admin' lets this contact manage the account's other contacts and addresses; 'Buyer'
	-- can quote and order and nothing else.
	[RoleInAccount] NVARCHAR(20) NOT NULL DEFAULT 'Buyer',

	-- The contact the account's own correspondence goes to. Exactly one per account is the
	-- intent; UQ_Contact_OnePrimary enforces it.
	[IsPrimary] BIT NOT NULL DEFAULT 0,

	-- 'Active' | 'Invited' | 'Disabled'. Separate from Account.Status: a suspended account
	-- disables all its contacts, but one contact leaving the company must not disable the rest.
	[Status] NVARCHAR(20) NOT NULL DEFAULT 'Active',

	[CreatedDate] DATETIME2 NOT NULL DEFAULT getutcdate(),

	CONSTRAINT [FK_Contact_ToAccount] FOREIGN KEY ([AccountId]) REFERENCES [Account]([Id]),

	-- Scoped to the account, not global. The same person may hold a login at two of the
	-- stores run from this platform, and those are separate customers of separate businesses
	-- that have no business learning about each other.
	CONSTRAINT [UQ_Contact_Email] UNIQUE ([AccountId], [Email]),

	-- New tables get their closed value sets enforced. The older columns of this kind
	-- (Account.Status, GroupVisibility.Rule, Site.OrderMode) are documented in comments only,
	-- and were not retrofitted here; the case for a constraint is strongest where the value
	-- gates behaviour, and a mistyped role silently grants or withholds permission.
	CONSTRAINT [CK_Contact_RoleInAccount] CHECK ([RoleInAccount] IN ('Admin', 'Buyer')),
	CONSTRAINT [CK_Contact_Status] CHECK ([Status] IN ('Active', 'Invited', 'Disabled'))
)
GO

-- One primary contact per account, expressed as a filtered index because a unique constraint
-- would allow only one non-primary contact as well.
CREATE UNIQUE NONCLUSTERED INDEX [UQ_Contact_OnePrimary]
	ON [dbo].[Contact] ([AccountId])
	WHERE [IsPrimary] = 1;
GO

-- Sign-in resolves a contact by the Identity user it authenticated as, on every request that
-- needs the account behind it.
CREATE NONCLUSTERED INDEX [IX_Contact_IdentityUser]
	ON [dbo].[Contact] ([IdentityUserId])
	INCLUDE ([AccountId], [Status]);
