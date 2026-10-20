using System.Collections.Generic;

namespace SMDataManager.Library.Models
{
    /// <summary>
    /// Everything that narrows a catalog listing. Passed as one object rather than eight
    /// parameters so adding a facet later does not change every call site.
    /// </summary>
    public class CatalogQuery
    {
        public int SiteId { get; set; }

        /// <summary>
        /// The signed-in account's group, or null for an anonymous visitor. Drives visibility
        /// rules; null means only the rules that apply to everyone.
        /// </summary>
        public int? CustomerGroupId { get; set; }

        public string CategorySlug { get; set; }
        public string Brand { get; set; }
        public bool InStockOnly { get; set; }
        public string Search { get; set; }

        /// <summary>"featured" (default), "price-asc", "price-desc" or "name".</summary>
        public string Sort { get; set; }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 24;

        /// <summary>
        /// The option values a shopper filtered by (T11), Size M and Colour Navy. Several values
        /// for one name mean any of them.
        /// </summary>
        public List<CatalogOption> Options { get; set; } = new List<CatalogOption>();
    }

    /// <summary>One option value: a name in the store's own words, and one of its values.</summary>
    public sealed record CatalogOption(string Name, string Value);

    /// <summary>How many styles have a variant with this value, within the current filter.</summary>
    public class CatalogOptionFacetModel
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public int Count { get; set; }
    }

    public class CatalogFacetModel
    {
        public string Slug { get; set; }
        public string Name { get; set; }
        public int Count { get; set; }
    }

    public class CatalogFacets
    {
        public List<CatalogFacetModel> Categories { get; set; } = new List<CatalogFacetModel>();
        public List<CatalogFacetModel> Brands { get; set; } = new List<CatalogFacetModel>();
        public List<CatalogOptionFacetModel> Options { get; set; } = new List<CatalogOptionFacetModel>();
    }

    public class CatalogPage
    {
        public List<CatalogItemModel> Items { get; set; } = new List<CatalogItemModel>();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }

        public int TotalPages =>
            PageSize <= 0 ? 0 : (TotalCount + PageSize - 1) / PageSize;
    }
}
