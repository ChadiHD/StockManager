using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;

namespace SMDataManager.Library.DataAccess
{
    public class CategoryMappingData : ICategoryMappingData
    {
        /// <summary>spCategoryMapping_Set's refusal: the category is not this store's.</summary>
        private const int ForeignCategory = 50072;

        private readonly ISqlDataAccess _sqlDataAccess;

        public CategoryMappingData(ISqlDataAccess sqlDataAccess)
        {
            _sqlDataAccess = sqlDataAccess;
        }

        public List<FeedCategoryModel> GetFeedCategories(int siteId)
        {
            return _sqlDataAccess.LoadData<FeedCategoryModel, dynamic>(
                "dbo.spCategoryMapping_GetForSite", new { SiteId = siteId }, "SMDatabase");
        }

        public List<SiteCategoryModel> GetSiteCategories(int siteId)
        {
            return _sqlDataAccess.LoadData<SiteCategoryModel, dynamic>(
                "dbo.spSiteCategory_GetBySite", new { SiteId = siteId }, "SMDatabase");
        }

        public string Map(int siteId, string feedValue, int? siteCategoryId)
        {
            try
            {
                _sqlDataAccess.SaveData("dbo.spCategoryMapping_Set", new
                {
                    SiteId = siteId,
                    FeedValue = feedValue,
                    SiteCategoryId = siteCategoryId
                }, "SMDatabase");

                return null;
            }
            catch (SqlException ex) when (ex.Number == ForeignCategory)
            {
                return ex.Message;
            }
        }
    }
}
