using System;
using System.ComponentModel.DataAnnotations;

namespace SMDesktopUI.Library.Models
{
    public class InventoryModel
    {
        [Range(1, int.MaxValue, ErrorMessage = "Enter a valid product ID.")]
        public int ProductId { get; set; }
        [Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least one.")]
        public int Quantity { get; set; }
        [Range(typeof(decimal), "0.01", "79228162514264337593543950335", ErrorMessage = "Purchase price must be greater than zero.")]
        public decimal PurchasePrice { get; set; }
        public DateTime PurchaseDate { get; set; }
    }
}
