using Microsoft.AspNet.Identity;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity.Core.Metadata.Edm;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace SMDataManager.Controllers
{
    [Authorize]
    public class PurchaseController : ApiController
    {
        [Authorize(Roles = "Staff")]
        public void Post(PurchaseModel purchase)
        {
            PurchaseData data = new PurchaseData();
            // request the staffId from the API directly (much more secure)
            string userId = RequestContext.Principal.Identity.GetUserId();

            data.SavePurchases(purchase, userId);
        }

        [Authorize(Roles = "Admin,Manager")]
        [Route("GetPurchaseReport")]
        public List<PurchaseReportModel> GetPurchaseReports()
        {
            PurchaseData data = new PurchaseData();
            return data.GetPurchaseReport();
        }
    }
}
