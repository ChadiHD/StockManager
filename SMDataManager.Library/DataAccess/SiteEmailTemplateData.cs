using System.Linq;
using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;

namespace SMDataManager.Library.DataAccess
{
    public class SiteEmailTemplateData : ISiteEmailTemplateData
    {
        private readonly ISqlDataAccess _sqlDataAccess;

        public SiteEmailTemplateData(ISqlDataAccess sqlDataAccess)
        {
            _sqlDataAccess = sqlDataAccess;
        }

        public SiteEmailTemplateModel Get(int siteId, string templateKey)
        {
            return _sqlDataAccess.LoadData<SiteEmailTemplateModel, dynamic>(
                "dbo.spSiteEmailTemplate_Get",
                new { SiteId = siteId, TemplateKey = templateKey },
                "SMDatabase").FirstOrDefault();
        }
    }
}
