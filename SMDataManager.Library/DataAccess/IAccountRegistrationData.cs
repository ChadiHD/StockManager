using SMDataManager.Library.Models;

namespace SMDataManager.Library.DataAccess
{
    /// <summary>
    /// The one write path by which a stranger becomes a trading account.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="IAccountData"/> on purpose. Everything there is an
    /// administrator acting on an existing account and takes a siteId because an admin may be
    /// acting for any of several stores; this is a member of the public, and the site comes
    /// from the request host rather than from a header a caller chose.
    ///
    /// The account it creates is always Pending. Nothing an applicant types decides whether
    /// they may trade.
    /// </remarks>
    public interface IAccountRegistrationData
    {
        AccountRegistrationResult Register(AccountRegistration registration);
    }
}
