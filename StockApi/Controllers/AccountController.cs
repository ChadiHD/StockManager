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
using StockApi.Accounts;
using StockApi.Sites;
using StockManager.Notifications;

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
        private readonly IEmailSender _email;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            IAccountData accountData,
            IAdminSiteContext site,
            IEmailSender email,
            ILogger<AccountController> logger)
        {
            _accountData = accountData;
            _site = site;
            _email = email;
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
        public async Task<IActionResult> Approve(
            int id, ApprovalModel approval, CancellationToken cancellationToken)
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

            await NotifyAsync(
                AccountDecisionEmails.Approved(_site.Site, Reread(id) ?? account),
                id, cancellationToken);

            return NoContent();
        }

        public record RejectionModel(string Reason);

        [HttpPost("{id:int}/Reject")]
        public async Task<IActionResult> Reject(
            int id, RejectionModel rejection, CancellationToken cancellationToken)
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

            await NotifyAsync(
                AccountDecisionEmails.Rejected(_site.Site, account, rejection.Reason.Trim()),
                id, cancellationToken);

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

        private AccountModel Reread(int id) => _accountData.GetAccountById(id, _site.SiteId);

        /// <summary>
        /// Sends the decision mail without letting it undo the decision.
        /// </summary>
        /// <remarks>
        /// The write has already committed. Throwing here would return a failure for a
        /// decision that was in fact made, and the portal would re-issue it — landing on the
        /// Conflict above and reading as a bug. So a failed notification is logged with the
        /// account id and swallowed, and chasing it is an operational job rather than the
        /// customer's problem. T6's outbox is what turns this into a retry.
        /// </remarks>
        private async Task NotifyAsync(EmailMessage message, int accountId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(message.To))
            {
                _logger.LogWarning(
                    "Account {AccountId} has no email address; the decision was not sent.", accountId);

                return;
            }

            try
            {
                await _email.SendAsync(message, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception,
                    "The decision on account {AccountId} was recorded but could not be emailed.",
                    accountId);
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
