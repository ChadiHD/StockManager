using System;

namespace SMDataManager.Library.Models
{
    /// <summary>
    /// One editorial page belonging to one store.
    /// </summary>
    public class SiteContentModel
    {
        public int Id { get; set; }
        public int SiteId { get; set; }

        /// <summary>Matches the storefront route slug — "about", "solutions", "terms".</summary>
        public string ContentKey { get; set; }

        /// <summary>Null on the fallback row used when the site's locale has none of its own.</summary>
        public string Locale { get; set; }

        public string Title { get; set; }
        public string Lede { get; set; }

        /// <summary>
        /// Rendered as markup by the storefront, so it is staff-authored only and must never
        /// carry anything that originated with a customer.
        /// </summary>
        public string BodyHtml { get; set; }

        public DateTime LastModified { get; set; }
    }
}
