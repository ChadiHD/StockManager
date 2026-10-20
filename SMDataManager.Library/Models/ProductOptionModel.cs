using System.Collections.Generic;

namespace SMDataManager.Library.Models
{
    /// <summary>One option of one variant (T11): Size = M. The name is the store's own word.</summary>
    public class ProductOptionModel
    {
        public int ProductId { get; set; }
        public string Name { get; set; }
        public string Value { get; set; }
        public int SortOrder { get; set; }
    }

    /// <summary>
    /// A style and the variants of it this viewer may buy, with their options, as
    /// spCatalog_GetFamily returns them. <see cref="Style"/> is null when the style itself is not
    /// on sale here, and then neither is any variant.
    /// </summary>
    public class CatalogFamily
    {
        public CatalogItemModel Style { get; set; }
        public List<CatalogItemModel> Variants { get; set; } = new();
        public List<ProductOptionModel> Options { get; set; } = new();
    }
}
