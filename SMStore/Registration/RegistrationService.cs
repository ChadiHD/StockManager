using Microsoft.AspNetCore.Identity;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using SMStore.Sites;
using StockManager.Identity;

namespace SMStore.Registration;

/// <summary>What came of a registration attempt.</summary>
/// <param name="Accepted">
/// Whether the applicant should be shown the "check your email" page. Deliberately not the
/// same thing as "an account was created" — see <see cref="RegistrationService"/>.
/// </param>
/// <param name="Reference">
/// The AC-nnnn reference, or null when nothing was created — which happens both on failure
/// and on the neutral answer to an address already registered here. A caller must not read
/// null as failure: <paramref name="Accepted"/> is the only thing that says what to render.
/// </param>
/// <param name="AccountId">
/// The rows just written, for a caller with something to attach to them. Null whenever
/// <paramref name="Reference"/> is, and for the same reasons — in particular, a duplicate
/// applicant gets no ids, because attaching their upload to the existing account would tell
/// whoever sent it that the account exists.
/// </param>
public sealed record RegistrationOutcome(
    bool Accepted,
    IReadOnlyList<RegistrationError> Errors,
    string? Reference = null,
    int? AccountId = null,
    int? ContactId = null);

public interface IRegistrationService
{
    Task<RegistrationOutcome> RegisterAsync(
        RegistrationSubmission submission, CancellationToken cancellationToken = default);
}

/// <summary>
/// Turns a filled-in registration form into a login and a pending trading account.
/// </summary>
/// <remarks>
/// The awkward part is that this spans two databases. The login lives in ApiAuthDb under
/// Entity Framework; the account, contact and address live in SMDatabase under Dapper and a
/// stored procedure. There is no transaction across them and there is not going to be — a
/// distributed transaction would mean MSDTC, which is not available on Azure SQL and is not
/// worth what it costs anywhere else.
///
/// So the order is: create the login, then write the account, and delete the login again if
/// that write fails. The window in which both can be wrong is one stored procedure call, and
/// that procedure is itself all-or-nothing. What remains is the case where the compensating
/// delete also fails, which cannot be made impossible — it is logged at Error with the
/// username, because a login with no account blocks that person from ever registering again
/// and nothing else will surface it.
/// </remarks>
public sealed class RegistrationService : IRegistrationService
{
    private readonly UserManager<IdentityUser> _users;
    private readonly IAccountRegistrationData _registrations;
    private readonly RegistrationFieldSetProvider _fieldSets;
    private readonly ISiteContext _siteContext;
    private readonly ILogger<RegistrationService> _logger;

    public RegistrationService(
        UserManager<IdentityUser> users,
        IAccountRegistrationData registrations,
        RegistrationFieldSetProvider fieldSets,
        ISiteContext siteContext,
        ILogger<RegistrationService> logger)
    {
        _users = users;
        _registrations = registrations;
        _fieldSets = fieldSets;
        _siteContext = siteContext;
        _logger = logger;
    }

    public async Task<RegistrationOutcome> RegisterAsync(
        RegistrationSubmission submission, CancellationToken cancellationToken = default)
    {
        var site = _siteContext.Site;
        var fieldSet = _fieldSets.Current;

        // The server-side check, and the only one that counts. Whatever the browser did is a
        // convenience.
        var errors = fieldSet.Validate(submission, site).ToList();

        if (errors.Any(error => error.Blocks))
        {
            return new RegistrationOutcome(false, errors);
        }

        var email = submission.Email!.Trim();
        var userName = SiteQualifiedUserName.For(site.SiteKey, email);

        /*
        An address already registered at this store gets the same answer as a new one.

        Saying "that email is already registered" turns this form into an oracle for whether
        a given company buys here, which is commercially useful to a competitor and needs no
        account to query. So nothing is created and the applicant is told what a successful
        applicant is told.

        This leaves a real duplicate applicant with no feedback, and the fix is the mail that
        says "someone tried to register with your address" — which belongs to the email work
        and is not built yet. Until it is, the acknowledgement page has to carry a "contact us
        if you hear nothing" line, or this is a dead end for anyone who forgot they applied.
        */
        if (await _users.FindByNameAsync(userName) is not null)
        {
            _logger.LogInformation(
                "Registration attempted for an address already registered at {SiteKey}. " +
                "Answered neutrally; nothing created.", site.SiteKey);

            return new RegistrationOutcome(true, errors);
        }

        var user = new IdentityUser
        {
            UserName = userName,
            Email = email,
            // Confirmation is a separate step. Creating the login is not evidence the address
            // belongs to the applicant, and approval must not treat it as such.
            EmailConfirmed = false
        };

        var created = await _users.CreateAsync(user, submission.Password!);

        if (!created.Succeeded)
        {
            // Password-policy failures are safe to show and useless to hide: the applicant
            // has to know why their password was refused. Anything mentioning the username is
            // dropped, because that is the enumeration this method just avoided.
            errors.AddRange(created.Errors
                .Where(error => !error.Code.Contains("UserName", StringComparison.OrdinalIgnoreCase)
                             && !error.Code.Contains("Email", StringComparison.OrdinalIgnoreCase))
                .Select(error => new RegistrationError(null, error.Description)));

            if (errors.All(error => !error.Blocks))
            {
                // Everything Identity objected to was filtered out above, so there is nothing
                // to show. Answer neutrally rather than reporting a success that did not
                // happen or an error with no message.
                _logger.LogWarning("Identity refused a registration for reasons that cannot be " +
                                   "shown to the applicant: {Codes}.",
                    string.Join(", ", created.Errors.Select(error => error.Code)));

                return new RegistrationOutcome(true, errors);
            }

            return new RegistrationOutcome(false, errors);
        }

        try
        {
            var result = _registrations.Register(new AccountRegistration
            {
                SiteId = site.Id,
                IdentityUserId = user.Id,
                Company = Trimmed(submission.Company),
                VatNumber = Trimmed(submission.VatNumber),
                RegistrationNumber = Trimmed(submission.RegistrationNumber),
                FirstName = Trimmed(submission.FirstName),
                LastName = Trimmed(submission.LastName),
                Email = email,
                Phone = Trimmed(submission.Phone),
                Line1 = Trimmed(submission.AddressLine1),
                Line2 = Trimmed(submission.AddressLine2),
                City = Trimmed(submission.City),
                Region = Trimmed(submission.Region),
                PostCode = Trimmed(submission.PostCode),
                Country = Trimmed(submission.Country)?.ToUpperInvariant()
            });

            _logger.LogInformation(
                "Registered {Reference} at {SiteKey}, pending approval.",
                result?.Reference, site.SiteKey);

            return new RegistrationOutcome(
                true, errors, result?.Reference, result?.AccountId, result?.ContactId);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception,
                "Writing the account for a new registration at {SiteKey} failed. " +
                "Removing the login that was created for it.", site.SiteKey);

            await CompensateAsync(user);

            return new RegistrationOutcome(false, [new RegistrationError(null,
                "Something went wrong setting up your account. Nothing has been saved — please try again.")]);
        }
    }

    /// <summary>
    /// Removes a login whose account could not be written, so the applicant can try again.
    /// </summary>
    /// <remarks>
    /// Failing here is the one outcome this design cannot rule out, and it is worse than the
    /// failure that caused it: the username stays taken, so the applicant retries and is told
    /// — neutrally, giving them nothing to act on — that they have already registered. Hence
    /// the username in the log. It is the only way anyone finds out.
    /// </remarks>
    private async Task CompensateAsync(IdentityUser user)
    {
        try
        {
            var deleted = await _users.DeleteAsync(user);

            if (!deleted.Succeeded)
            {
                _logger.LogError(
                    "Orphaned login {UserName} could not be deleted after a failed registration: " +
                    "{Errors}. It must be removed by hand or that applicant cannot register again.",
                    user.UserName, string.Join(", ", deleted.Errors.Select(error => error.Description)));
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception,
                "Orphaned login {UserName} could not be deleted after a failed registration. " +
                "It must be removed by hand or that applicant cannot register again.", user.UserName);
        }
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
