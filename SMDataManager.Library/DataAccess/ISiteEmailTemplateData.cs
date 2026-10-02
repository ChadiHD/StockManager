using SMDataManager.Library.Models;

namespace SMDataManager.Library.DataAccess
{
    public interface ISiteEmailTemplateData
    {
        /// <summary>The store's own wording for a message, or null when it uses the platform's.</summary>
        SiteEmailTemplateModel Get(int siteId, string templateKey);
    }
}
