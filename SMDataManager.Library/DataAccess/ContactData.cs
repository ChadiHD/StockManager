using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using System.Collections.Generic;
using System.Linq;

namespace SMDataManager.Library.DataAccess
{
    public class ContactData : IContactData
    {
        private readonly ISqlDataAccess _sqlDataAccess;

        public ContactData(ISqlDataAccess sqlDataAccess)
        {
            _sqlDataAccess = sqlDataAccess;
        }

        public List<ContactModel> GetByAccount(int accountId, int siteId)
        {
            return _sqlDataAccess.LoadData<ContactModel, dynamic>(
                "dbo.spContact_GetByAccount",
                new { AccountId = accountId, SiteId = siteId }, "SMDatabase");
        }

        public ContactModel GetByIdentityUser(string identityUserId, int siteId)
        {
            return _sqlDataAccess.LoadData<ContactModel, dynamic>(
                "dbo.spContact_GetByIdentityUser",
                new { IdentityUserId = identityUserId, SiteId = siteId },
                "SMDatabase").FirstOrDefault();
        }

        public ContactModel Insert(ContactModel contact, int siteId)
        {
            _sqlDataAccess.SaveData("dbo.spContact_Insert", new
            {
                Id = 0,
                contact.AccountId,
                contact.IdentityUserId,
                contact.FirstName,
                contact.LastName,
                contact.Email,
                contact.Phone,
                RoleInAccount = string.IsNullOrWhiteSpace(contact.RoleInAccount) ? "Buyer" : contact.RoleInAccount,
                contact.IsPrimary,
                Status = string.IsNullOrWhiteSpace(contact.Status) ? "Active" : contact.Status,
                SiteId = siteId
            }, "SMDatabase");

            // The identity value is assigned inside the procedure and Dapper cannot read an
            // output parameter back through an anonymous object, so re-read. Email is unique
            // per account, which makes it the one field that identifies the new row.
            return GetByAccount(contact.AccountId, siteId)
                .FirstOrDefault(candidate => candidate.Email == contact.Email);
        }

        public void Update(ContactModel contact, int siteId)
        {
            _sqlDataAccess.SaveData("dbo.spContact_Update", new
            {
                contact.Id,
                contact.FirstName,
                contact.LastName,
                contact.Email,
                contact.Phone,
                contact.RoleInAccount,
                contact.IsPrimary,
                contact.Status,
                SiteId = siteId
            }, "SMDatabase");
        }
    }
}
