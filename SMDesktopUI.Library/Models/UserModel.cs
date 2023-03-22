using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SMDesktopUI.Library.Models
{
    public class UserModel
    {
        public string Id { get; set; }
        public string Email { get; set; }
        public Dictionary<string, string> Roles { get; set; } = new Dictionary<string, string>();
        
        // Get all dictionary roles list and join them by comma
        public string RoleList
        {
            get 
            { 
                return string.Join(", ", Roles.Values);
            }
        }

    }
}
