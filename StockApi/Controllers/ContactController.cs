using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SMDataManager.Library.DataAccess;
using StockApi.Sites;

namespace StockApi.Controllers
{
    /// <summary>
    /// The people on a trading account, for the reviewer deciding whether to open it.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class ContactController : ControllerBase
    {
        private readonly IContactData _contacts;
        private readonly IAccountData _accounts;
        private readonly IAdminSiteContext _site;

        public ContactController(IContactData contacts, IAccountData accounts, IAdminSiteContext site)
        {
            _contacts = contacts;
            _accounts = accounts;
            _site = site;
        }

        /// <summary>One person on an account, without the login behind them.</summary>
        /// <remarks>
        /// IdentityUserId is deliberately absent. It is the primary key of a row in another
        /// database, it identifies a credential, and no admin screen has any use for it —
        /// publishing it would put it in browser memory and in every log that captured a
        /// response body, for nothing.
        /// </remarks>
        public record ContactListItem(
            int Id,
            int AccountId,
            string FirstName,
            string LastName,
            string Email,
            string Phone,
            string RoleInAccount,
            bool IsPrimary,
            string Status,
            DateTime CreatedDate);

        [HttpGet]
        public ActionResult<List<ContactListItem>> GetByAccount([FromQuery] int accountId)
        {
            // The contact procedures join Account for the site predicate, so a bad id returns
            // nothing on its own. Checking the account first is what turns that into a 404
            // rather than an empty list, which reads as "this customer has no contacts".
            if (_accounts.GetAccountById(accountId, _site.SiteId) is null)
            {
                return NotFound();
            }

            return _contacts.GetByAccount(accountId, _site.SiteId)
                .Select(contact => new ContactListItem(
                    contact.Id, contact.AccountId, contact.FirstName, contact.LastName,
                    contact.Email, contact.Phone, contact.RoleInAccount, contact.IsPrimary,
                    contact.Status, contact.CreatedDate))
                .ToList();
        }
    }
}
