using SMDataManager.Library.Models;
using System.Collections.Generic;

namespace SMDataManager.Library.DataAccess
{
    public interface ISiteData
    {
        List<SiteModel> GetSites();
        SiteModel GetSiteByDomain(string domain);
        SiteModel GetSiteByKey(string siteKey);

        /// <summary>One store's whole configuration, active or not; null when there is none.</summary>
        SiteSettingsModel GetSettings(int siteId);

        /// <summary>Saves a store's settings. Returns why the database refused, or null.</summary>
        string UpdateSettings(SiteModel site);

        /// <summary>Creates a store, closed (T9). Returns its id, or why the database refused.</summary>
        (int Id, string Refusal) CreateSite(SiteModel site);

        /// <summary>
        /// Opens or closes a store. Opening is refused, naming what is missing, until the
        /// store's checklist is met; see <c>spSite_SetActive</c>.
        /// </summary>
        string SetActive(int siteId, bool isActive);

        /// <summary>
        /// Stores a store's payment keys as ciphertext (T13), both or neither. Null when stored,
        /// otherwise why not.
        /// </summary>
        string SetPaymentSecrets(int siteId, string secretRef, string webhookSecretRef);

        /// <summary>A store's payment keys, as ciphertext. The storefront's read, never a screen's.</summary>
        SitePaymentSecrets GetPaymentSecrets(int siteId);
    }
}
