using SMDataManager.Library.Internal.DataAccess;
using SMDataManager.Library.Models;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SMDataManager.Library.DataAccess
{
    public class CustomerGroupData : ICustomerGroupData
    {
        private readonly ISqlDataAccess _sqlDataAccess;

        public CustomerGroupData(ISqlDataAccess sqlDataAccess)
        {
            _sqlDataAccess = sqlDataAccess;
        }

        public List<CustomerGroupModel> GetGroups()
        {
            return _sqlDataAccess.LoadData<CustomerGroupModel, dynamic>(
                "dbo.spCustomerGroup_GetAll", new { }, "SMDatabase");
        }

        public CustomerGroupModel GetGroupBySlug(string slug)
        {
            return _sqlDataAccess.LoadData<CustomerGroupModel, dynamic>(
                "dbo.spCustomerGroup_GetBySlug", new { Slug = slug }, "SMDatabase").FirstOrDefault();
        }

        public CustomerGroupModel CreateGroup(CustomerGroupModel group)
        {
            group.Slug = Slugify(group.Name);

            _sqlDataAccess.SaveData("dbo.spCustomerGroup_Insert", new
            {
                Id = 0,
                group.Name,
                group.Slug,
                group.Discount,
                group.Terms,
                group.Note
            }, "SMDatabase");

            return GetGroupBySlug(group.Slug);
        }

        public void UpdateGroup(string slug, int discount, string terms, string note)
        {
            _sqlDataAccess.SaveData("dbo.spCustomerGroup_Update", new
            {
                Slug = slug,
                Discount = discount,
                Terms = terms,
                Note = note
            }, "SMDatabase");
        }

        // Group names contain spaces and slashes ("Government / Education"), so the admin UI
        // routes on a slug instead. Kept here so the stored slug and the route always agree.
        public static string Slugify(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            bool lastWasDash = false;

            foreach (char character in value.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(character);
                    lastWasDash = false;
                }
                else if (!lastWasDash && builder.Length > 0)
                {
                    builder.Append('-');
                    lastWasDash = true;
                }
            }

            return builder.ToString().Trim('-');
        }
    }
}
