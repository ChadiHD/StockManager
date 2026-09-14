using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Sites;

namespace StockApi.Controllers
{
    /// <summary>
    /// Billing and delivery addresses on a trading account.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AddressController : ControllerBase
    {
        private readonly IAddressData _addresses;
        private readonly IAccountData _accounts;
        private readonly IAdminSiteContext _site;

        public AddressController(IAddressData addresses, IAccountData accounts, IAdminSiteContext site)
        {
            _addresses = addresses;
            _accounts = accounts;
            _site = site;
        }

        [HttpGet]
        public ActionResult<List<AddressModel>> GetByAccount([FromQuery] int accountId)
        {
            if (_accounts.GetAccountById(accountId, _site.SiteId) is null)
            {
                return NotFound();
            }

            return _addresses.GetByAccount(accountId, _site.SiteId);
        }
    }
}
