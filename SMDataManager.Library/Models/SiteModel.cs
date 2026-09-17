using System;

namespace SMDataManager.Library.Models
{
    /// <summary>
    /// A storefront tenant. Everything that differs between the stores run from this codebase
    /// lives here, so a second store is a row plus assets rather than a code change.
    /// </summary>
    public class SiteModel
    {
        public int Id { get; set; }

        /// <summary>
        /// Stable slug. Per-site assets live under wwwroot/sites/{SiteKey}/, so this is part of
        /// a URL and must not change once a site is live — unlike <see cref="Domain"/>.
        /// </summary>
        public string SiteKey { get; set; }

        public string Name { get; set; }
        public string Domain { get; set; }
        public string Country { get; set; }
        public string CurrencyCode { get; set; }
        public string Locale { get; set; }

        /// <summary>Selects the ordering behaviour: "Rfq" or "DirectCheckout".</summary>
        public string OrderMode { get; set; }

        /// <summary>Names the customer-application field set to render, e.g. "eu-b2b".</summary>
        public string RegistrationFieldSet { get; set; }

        /// <summary>"Public" or "Authenticated" — whether anonymous visitors see list prices.</summary>
        public string PriceDisplay { get; set; }

        /// <summary>
        /// Floor under group discounting: a resolved price never falls below cost plus this
        /// margin. Zero disables it.
        /// </summary>
        public decimal MinMarginPct { get; set; }

        /// <summary>
        /// How old a distributor-sourced product's <c>LastSynced</c> may be before it counts as
        /// stale. Zero disables staleness.
        /// </summary>
        /// <remarks>
        /// Per site because the threshold is a commercial judgement about this store's
        /// distributor rather than a platform constant. Read by the alerting pass; the
        /// storefront never applies it in C# — <c>dbo.fnSite_StaleBeforeUtc</c> resolves the
        /// cutoff inside the catalog procedures, because a product filtered out after the query
        /// has still been counted and still shifted the paging.
        /// </remarks>
        public int FeedStaleAfterHours { get; set; }

        /// <summary>
        /// Whether a stale product disappears from the storefront, or only shows up in the
        /// operator's alerts.
        /// </summary>
        public bool HideStaleProducts { get; set; }

        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}
