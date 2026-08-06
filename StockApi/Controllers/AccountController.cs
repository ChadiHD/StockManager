using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;

namespace StockApi.Controllers
{
    // Trading accounts (customer companies) behind the admin portal.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AccountController : ControllerBase
    {
        private readonly IAccountData _accountData;

        public AccountController(IAccountData accountData)
        {
            _accountData = accountData;
        }

        [HttpGet]
        public List<AccountModel> GetAll()
        {
            return _accountData.GetAccounts();
        }

        [HttpGet("{id:int}")]
        public ActionResult<AccountModel> GetById(int id)
        {
            var account = _accountData.GetAccountById(id);

            return account is null ? NotFound() : account;
        }

        [HttpPost]
        public ActionResult<AccountModel> Create(AccountModel account)
        {
            if (string.IsNullOrWhiteSpace(account.Company))
            {
                return BadRequest("Company is required.");
            }

            return _accountData.CreateAccount(account);
        }

        public record StatusChangeModel(string Status);

        [HttpPut("{id:int}/Status")]
        public IActionResult UpdateStatus(int id, StatusChangeModel change)
        {
            if (_accountData.GetAccountById(id) is null)
            {
                return NotFound();
            }

            _accountData.UpdateStatus(id, change.Status);

            return NoContent();
        }

        public record TermsChangeModel(
            int? CustomerGroupId,
            string PaymentMethod,
            string PaymentTerms,
            decimal CreditLimit);

        [HttpPut("{id:int}/Terms")]
        public IActionResult UpdateTerms(int id, TermsChangeModel change)
        {
            if (_accountData.GetAccountById(id) is null)
            {
                return NotFound();
            }

            _accountData.UpdateTerms(id, change.CustomerGroupId, change.PaymentMethod,
                change.PaymentTerms, change.CreditLimit);

            return NoContent();
        }
    }
}
