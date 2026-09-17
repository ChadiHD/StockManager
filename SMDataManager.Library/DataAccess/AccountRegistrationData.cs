using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using System.Linq;

namespace SMDataManager.Library.DataAccess
{
    public class AccountRegistrationData : IAccountRegistrationData
    {
        private readonly ISqlDataAccess _sqlDataAccess;

        public AccountRegistrationData(ISqlDataAccess sqlDataAccess)
        {
            _sqlDataAccess = sqlDataAccess;
        }

        public AccountRegistrationResult Register(AccountRegistration registration)
        {
            // LoadData rather than SaveData: the procedure selects the ids it generated, and
            // an output parameter would be unreadable through Dapper's anonymous object.
            return _sqlDataAccess.LoadData<AccountRegistrationResult, dynamic>(
                "dbo.spAccount_Register", new
                {
                    registration.SiteId,
                    registration.IdentityUserId,
                    registration.Company,
                    registration.VatNumber,
                    registration.RegistrationNumber,
                    registration.FirstName,
                    registration.LastName,
                    registration.Email,
                    registration.Phone,
                    registration.Line1,
                    registration.Line2,
                    registration.City,
                    registration.Region,
                    registration.PostCode,
                    registration.Country
                }, "SMDatabase").FirstOrDefault();
        }
    }
}
