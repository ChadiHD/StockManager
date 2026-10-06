using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SMDataManager.Library.Models
{
    public class UserModel
    {
        public string UserId { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string EmailAddress { get; set; }
        public DateTime CreatedDate { get; set; }

        /// <summary>
        /// May act for every store in the admin portal. Otherwise only those in
        /// <c>dbo.UserSite</c>; see <see cref="UserSiteModel"/>.
        /// </summary>
        public bool AllSites { get; set; }
    }

    /// <summary>One store a member of staff has been given.</summary>
    public class UserSiteModel
    {
        public string UserId { get; set; }
        public int SiteId { get; set; }
    }
}
