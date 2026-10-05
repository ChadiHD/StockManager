namespace SMDataManager.Library.Models
{
    /// <summary>
    /// One category string as the distributor feeds file it, and where one store files it.
    /// </summary>
    public class FeedCategoryModel
    {
        /// <summary>Matched against dbo.Product.Category exactly.</summary>
        public string FeedValue { get; set; }

        /// <summary>Live products carrying it — delisted ones are not counted.</summary>
        public int Products { get; set; }

        /// <summary>The store's category it maps to, or null when the store does not sell it.</summary>
        public int? SiteCategoryId { get; set; }
        public string SiteCategoryName { get; set; }
    }
}
