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

	[IsActive] BIT NOT NULL DEFAULT 1,
	[CreatedDate] DATETIME2 NOT NULL DEFAULT getutcdate(),

	CONSTRAINT [UQ_Site_SiteKey] UNIQUE ([SiteKey]),
	-- Host resolution looks a site up by domain, so a domain must name exactly one site.
	CONSTRAINT [UQ_Site_Domain] UNIQUE ([Domain])
)
