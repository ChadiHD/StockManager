using Microsoft.Data.SqlClient;
using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using System.Collections.Generic;
using System.Linq;

namespace SMDataManager.Library.DataAccess
{
    public class SiteData : ISiteData
    {
        private readonly ISqlDataAccess _sqlDataAccess;

        public SiteData(ISqlDataAccess sqlDataAccess)
        {
            _sqlDataAccess = sqlDataAccess;
        }

        public List<SiteModel> GetSites()
        {
            return _sqlDataAccess.LoadData<SiteModel, dynamic>(
                "dbo.spSite_GetAll", new { }, "SMDatabase");
        }

        /// <summary>
        /// Resolves a request host to a site. Returns null for an unknown or inactive domain —
        /// the caller decides what that means, which is a 404 in the storefront.
        /// </summary>
        public SiteModel GetSiteByDomain(string domain)
        {
            return _sqlDataAccess.LoadData<SiteModel, dynamic>(
                "dbo.spSite_GetByDomain", new { Domain = domain }, "SMDatabase").FirstOrDefault();
        }

        public SiteModel GetSiteByKey(string siteKey)
        {
            return _sqlDataAccess.LoadData<SiteModel, dynamic>(
                "dbo.spSite_GetByKey", new { SiteKey = siteKey }, "SMDatabase").FirstOrDefault();
        }

        public SiteSettingsModel GetSettings(int siteId)
        {
            return _sqlDataAccess.LoadData<SiteSettingsModel, dynamic>(
                "dbo.spSite_GetSettings", new { SiteId = siteId }, "SMDatabase").FirstOrDefault();
        }

        public string UpdateSettings(SiteModel site)
        {
            try
            {
                _sqlDataAccess.SaveData("dbo.spSite_Update", new
                {
                    SiteId = site.Id,
                    site.Name,
                    site.Domain,
                    site.Country,
                    site.CurrencyCode,
                    site.Locale,
                    site.OrderMode,
                    site.RegistrationFieldSet,
                    site.PriceDisplay,
                    site.MinMarginPct,
                    site.FeedStaleAfterHours,
                    site.HideStaleProducts,
                    site.OperatorEmail,
                    site.TaxRuleSet,
                    site.StandardTaxRatePct,
                    site.TaxRegistrationNumber,
                    site.MailFromAddress,
                    site.LegalName,
                    site.CompanyRegistrationNumber,
                    site.RegisteredAddress
                }, "SMDatabase");

                return null;
            }
            // spSite_Update's refusals are sentences for the admin; anything else is a fault.
            catch (SqlException ex) when (ex.Number is >= 50090 and <= 50099)
            {
                return ex.Message;
            }
        }
    }
}
