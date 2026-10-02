using System;
using System.Security.Claims;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using StockApi.Sites;

namespace StockApi.Controllers
{
    // Trading accounts (customer companies) behind the admin portal.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AccountController : ControllerBase
    {
        private readonly IAccountData _accountData;
        private readonly IAdminSiteContext _site;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            IAccountData accountData,
            IAdminSiteContext site,
            ILogger<AccountController> logger)
        {
            _accountData = accountData;
            _site = site;
            _logger = logger;
        }

        [HttpGet]
        public List<AccountModel> GetAll()
        {
            return _accountData.GetAccounts(_site.SiteId);
        }

        [HttpGet("{id:int}")]
        public ActionResult<AccountModel> GetById(int id)
        {
            var account = _accountData.GetAccountById(id, _site.SiteId);

            return account is null ? NotFound() : account;
        }

        [HttpPost]
        public ActionResult<AccountModel> Create(AccountModel account)
        {
            if (string.IsNullOrWhiteSpace(account.Company))
            {
                return BadRequest("Company is required.");
            }

            return _accountData.CreateAccount(account, _site.SiteId);
        }

        public record StatusChangeModel(string Status);

        [HttpPut("{id:int}/Status")]
        public IActionResult UpdateStatus(int id, StatusChangeModel change)
        {
            if (_accountData.GetAccountById(id, _site.SiteId) is null)
            {
                return NotFound();
            }

            _accountData.UpdateStatus(id, change.Status, _site.SiteId);

            return NoContent();
        }

        public record ApprovalModel(int? CustomerGroupId);

        /// <summary>
        /// Approves a trading application: records who decided, assigns the pricing group,
        /// and tells the applicant they can sign in.
        /// </summary>
        /// <remarks>
        /// Not <c>UpdateStatus("Approved")</c>, which is what the portal used to call. That
        /// wrote a status and recorded nothing, and approval is the one transition that has
        /// to leave evidence — a customer eventually asks why they were let in on these
        /// terms, and "someone changed a status" is not an answer.
        ///
        /// Conflict rather than success when the procedure changes no row: the account was
        /// already approved, and the caller is looking at a stale screen. Reporting success
        /// would have the portal show an approval that this request did not make and an
        /// approver it did not record.
        /// </remarks>
        [HttpPost("{id:int}/Approve")]
        public IActionResult Approve(int id, ApprovalModel approval)
        {
            var account = _accountData.GetAccountById(id, _site.SiteId);

            if (account is null)
            {
                return NotFound();
            }

            if (Decider() is not { Length: > 0 } approver)
            {
                return Unauthorized("The signed-in operator has no user id to record as the approver.");
            }

            if (!_accountData.Approve(id, approver, approval.CustomerGroupId, _site.SiteId))
            {
                return Conflict("That account is not awaiting a decision.");
            }

            WarnIfUnaddressed(account);

            return NoContent();
        }

        public record RejectionModel(string Reason);

        [HttpPost("{id:int}/Reject")]
        public IActionResult Reject(int id, RejectionModel rejection)
        {
            // The procedure refuses a blank reason too. Checking here as well turns a
            // THROW into a 400 the portal can render against the textarea.
            if (string.IsNullOrWhiteSpace(rejection.Reason))
            {
                return BadRequest("A rejection needs a reason — it is what the applicant is told.");
            }

            var account = _accountData.GetAccountById(id, _site.SiteId);

            if (account is null)
            {
                return NotFound();
            }

            if (Decider() is not { Length: > 0 } decider)
            {
                return Unauthorized("The signed-in operator has no user id to record as the decider.");
            }

            if (!_accountData.Reject(id, decider, rejection.Reason.Trim(), _site.SiteId))
            {
                return Conflict("That account is not awaiting a decision.");
            }

            WarnIfUnaddressed(account);

            return NoContent();
        }

        /// <summary>
        /// Who is recorded as having decided: the operator's Identity user id.
        /// </summary>
        /// <remarks>
        /// From the authenticated principal and nowhere else. A decider supplied in the
        /// request body would be an audit trail the auditee writes.
        /// <para>
        /// It must be the id and not the name. <c>Account.ApprovedBy</c> is
        /// <c>FK_Account_ApprovedBy</c> into <c>dbo.User(UserId)</c>, so an email address —
        /// which is what <c>User.Identity.Name</c> holds here, the JWT being issued with the
        /// address as its name claim — violates the constraint and every approval fails with
        /// error 547. That is how this shipped: the unit tests asserted the approver came from
        /// <c>Identity.Name</c>, which was precisely the bug, and only the end-to-end journey
        /// through the real portal and the real database caught it.
        /// </para>
        /// Null when the principal carries no id, which <c>[Authorize]</c> should already have
        /// prevented — the callers turn it into a refusal rather than writing "unknown", which
        /// is not a user id either and breaks the same constraint.
        /// </remarks>
        private string? Decider() => User.FindFirstValue(ClaimTypes.NameIdentifier);

        /// <summary>
        /// Says so when a decision was recorded and nobody can be told about it.
        /// </summary>
        /// <remarks>
        /// The message itself is no longer this controller's: <c>spAccount_Approve</c> and
        /// <c>spAccount_Reject</c> queue it in the transaction that makes the decision, so a
        /// host dying after the commit can no longer lose it, and a mail relay that is down
        /// can no longer fail the request. What the procedures cannot do is log, and an
        /// account with no address — keyed in by hand rather than registered — gets no row at
        /// all. This is the record of that.
        /// </remarks>
        private void WarnIfUnaddressed(AccountModel account)
        {
            if (string.IsNullOrWhiteSpace(account.Email))
            {
                _logger.LogWarning(
                    "Account {AccountId} has no email address; the decision was recorded and " +
                    "nobody was told.", account.Id);
            }
        }

        public record TermsChangeModel(
            int? CustomerGroupId,
            string PaymentMethod,
            string PaymentTerms,
            decimal CreditLimit);

        [HttpPut("{id:int}/Terms")]
        public IActionResult UpdateTerms(int id, TermsChangeModel change)
        {
            // Refused here because the terms label and PaymentTermsDays have to agree, and
            // CK_Account_Terms enforcing that from inside a procedure would reach the caller
            // as a 500. Same boundary check, same reason, as QuoteController's status guard.
            if (!PaymentTerms.IsKnown(change.PaymentTerms))
            {
                return BadRequest(new
                {
                    change.PaymentTerms,
                    Message = $"Payment terms are one of: {string.Join(", ", PaymentTerms.All)}."
                });
            }

            if (_accountData.GetAccountById(id, _site.SiteId) is null)
            {
                return NotFound();
            }

            _accountData.UpdateTerms(id, change.CustomerGroupId, change.PaymentMethod,
                change.PaymentTerms, change.CreditLimit, _site.SiteId);

            return NoContent();
        }
    }
}
