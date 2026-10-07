using System.Collections.Generic;
using SMDataManager.Library.Models;

namespace SMDataManager.Library.DataAccess
{
    public interface ICategoryMappingData
    {
        /// <summary>Every feed category with live products, and where it files on the store.</summary>
        List<FeedCategoryModel> GetFeedCategories(int siteId);

        /// <summary>The store's own categories, for choosing where a feed category files.</summary>
        List<SiteCategoryModel> GetSiteCategories(int siteId);

        /// <summary>
        /// Files a feed category under one of the store's categories, or unmaps it when
        /// <paramref name="siteCategoryId"/> is null. Null when saved, otherwise why it was refused.
        /// </summary>
        string Map(int siteId, string feedValue, int? siteCategoryId);

        /// <summary>
        /// Creates a store category (<c>Id</c> 0) or edits one. Returns the saved id, or why the
        /// database refused.
        /// </summary>
        (int Id, string Refusal) SaveCategory(int siteId, SiteCategoryModel category);
    }
}
