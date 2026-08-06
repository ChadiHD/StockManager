using SMDataManager.Library.Models;
using System.Collections.Generic;

namespace SMDataManager.Library.DataAccess
{
    public interface ICustomerGroupData
    {
        List<CustomerGroupModel> GetGroups();
        CustomerGroupModel GetGroupBySlug(string slug);
        CustomerGroupModel CreateGroup(CustomerGroupModel group);
        void UpdateGroup(string slug, int discount, string terms, string note);
    }
}
