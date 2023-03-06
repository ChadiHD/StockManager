using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SMDesktopUI.Library.Models
{
    public class PurchaseModel
    {
        public List<PurchaseDetailModel> PurchaseDetails { get; set; } = new List<PurchaseDetailModel>();
    }
}