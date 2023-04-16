using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using System.Data;
using System.Security.Claims;

namespace StockApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class PurchaseController : ControllerBase
    {
        private readonly IPurchaseData _purchaseData;

        public PurchaseController(IPurchaseData purchaseData)
        {
            _purchaseData = purchaseData;
        }

        [Authorize(Roles = "Staff")]
        [HttpPost]
        public void Post(PurchaseModel purchase)
        {
            // request the staffId from the API directly (much more secure)
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier); //RequestContext.Principal.Identity.GetUserId();

            _purchaseData.SavePurchases(purchase, userId);
        }

        [Authorize(Roles = "Admin,Manager")]
        [Route("GetPurchaseReport")]
        [HttpGet]
        public List<PurchaseReportModel> GetPurchaseReports()
        {
            return _purchaseData.GetPurchaseReport();
        }
    }
}
