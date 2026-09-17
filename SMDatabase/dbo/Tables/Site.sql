-- A storefront tenant. StockManager is a template from which several stores are run, so
-- anything that differs between stores — domain, country, tax rules, branding, how orders are
-- placed — belongs here rather than in code or appsettings. One deployment of SMStore serves
-- every row, resolving the site from the request host.
--
-- SiteKey is the stable slug used to locate per-site assets on disk (wwwroot/sites/{SiteKey}/)
-- and to name configuration sections. Domain can change; SiteKey should not.
CREATE TABLE [dbo].[Site]
(
	[Id] INT NOT NULL PRIMARY KEY IDENTITY,
	[SiteKey] NVARCHAR(50) NOT NULL,
	[Name] NVARCHAR(200) NOT NULL,
	[Domain] NVARCHAR(253) NOT NULL,
	[Country] NVARCHAR(2) NOT NULL,
	[CurrencyCode] NVARCHAR(3) NOT NULL DEFAULT 'EUR',
	[Locale] NVARCHAR(10) NOT NULL DEFAULT 'en-IE',

	-- Selects the IOrderingMode implementation: 'Rfq' quotes everything through sales,
	-- 'DirectCheckout' takes an order straight from the cart. Only 'Rfq' exists today.
	[OrderMode] NVARCHAR(20) NOT NULL DEFAULT 'Rfq',

	-- Names the registration field set. Which fields a customer application demands is
	-- jurisdictional (a VAT number and a Chamber of Commerce document are an EU thing), so the
	-- form is driven by this key rather than hardcoded.
	[RegistrationFieldSet] NVARCHAR(50) NOT NULL DEFAULT 'eu-b2b',

	-- 'Public' shows list prices to anonymous visitors, 'Authenticated' hides them until sign-in.
	-- Differs per store, so it is not a global setting.
	[PriceDisplay] NVARCHAR(20) NOT NULL DEFAULT 'Public',

	-- Floor under group discounting: a resolved price never falls below cost plus this margin.
	-- A commercial policy of the store rather than of a group, because it exists to stop any
	-- group's discount from selling stock at a loss. Zero disables the floor.
	[MinMarginPct] DECIMAL(5, 2) NOT NULL DEFAULT 0,

	-- How old a distributor-sourced product's LastSynced may be before it counts as stale.
	-- Zero disables staleness entirely.
	--
	-- Per site because the threshold is a commercial judgement, not a platform constant: a
	-- distributor dropping a file nightly makes 26 hours suspicious, one delivering twice a
	-- week makes it normal. That is a property of this store's relationship with its
	-- distributor, so it belongs here rather than in appsettings.json, which would apply one
	-- number to every tenant.
	[FeedStaleAfterHours] INT NOT NULL DEFAULT 0,

	-- Whether a stale product disappears from the storefront, or only shows up in the
	-- operator's alerts. Off by default, with FeedStaleAfterHours at zero, so this lands inert:
	-- a platform-wide default that started hiding products on upgrade would be the wrong way
	-- round. See dbo.fnSite_StaleBeforeUtc.
	[HideStaleProducts] BIT NOT NULL DEFAULT 0,

	-- Where an operational alert about this store goes: a failed feed sync, or a feed whose
	-- data has gone stale. Per site because the back office is per store, and one address for
	-- the whole platform would tell every tenant's operator about every other tenant's
	-- distributor.
	--
	-- NULL means log only. Nothing invents an address, and nothing falls back to a
	-- platform-wide one — an alert about store A arriving at store B's inbox names A's
	-- distributor and A's hostname.
	[OperatorEmail] NVARCHAR(320) NULL,

	[IsActive] BIT NOT NULL DEFAULT 1,
	[CreatedDate] DATETIME2 NOT NULL DEFAULT getutcdate(),

	CONSTRAINT [UQ_Site_SiteKey] UNIQUE ([SiteKey]),
	-- Host resolution looks a site up by domain, so a domain must name exactly one site.
	CONSTRAINT [UQ_Site_Domain] UNIQUE ([Domain])
)
