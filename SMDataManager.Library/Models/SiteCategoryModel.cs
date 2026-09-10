namespace SMDataManager.Library.Models
{
    public class SiteCategoryModel
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public string Slug { get; set; }
        public string Name { get; set; }
        public string Blurb { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }

        /// <summary>How many feed values currently file into this category.</summary>
        public int MappedFeedValues { get; set; }
    }

    /// <summary>
    /// A feed category value nothing maps for this store. Each row is stock the store cannot
    /// sell until someone files it, which is why the count travels with it.
    /// </summary>
    public class UnmappedCategoryModel
    {
        public string FeedValue { get; set; }
        public int ProductCount { get; set; }
        public string Distributor { get; set; }
    }
}
