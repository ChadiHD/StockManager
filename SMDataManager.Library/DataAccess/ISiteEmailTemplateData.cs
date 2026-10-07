using SMDataManager.Library.Models;

namespace SMDataManager.Library.DataAccess
{
    public interface ISiteEmailTemplateData
    {
        /// <summary>The store's own wording for a message, or null when it uses the platform's.</summary>
        SiteEmailTemplateModel Get(int siteId, string templateKey);

        /// <summary>
        /// Sets the store's own wording (T9). Check it with <c>EmailRenderer.WhyRefused</c> first.
        /// </summary>
        void Save(int siteId, string templateKey, string subject, string body);

        /// <summary>Goes back to the platform's wording for the message.</summary>
        void Delete(int siteId, string templateKey);
    }
}
