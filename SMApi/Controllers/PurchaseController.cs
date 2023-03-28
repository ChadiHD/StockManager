using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using System.Collections.Generic;
using System.Security.Claims;

namespace SMApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class PurchaseController : ControllerBase
    {
        [Authorize(Roles = "Staff")]
        public void Post(PurchaseModel purchase)
        {
            PurchaseData data = new PurchaseData();
            // request the staffId from the API directly (much more secure)
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier); //RequestContext.Principal.Identity.GetUserId();

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
