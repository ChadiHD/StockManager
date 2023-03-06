using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace SMDataManager.Library.Models
{
    public class PurchaseDBModel
    {
        public int Id { get; set; }
        public string StaffId { get; set; }
        public DateTime PurchaseDate { get; set; } = DateTime.UtcNow;
        public decimal SubTotal { get; set; }
        public decimal VAT { get; set; }
        public decimal FinalPrice { get; set; }
    }
}
